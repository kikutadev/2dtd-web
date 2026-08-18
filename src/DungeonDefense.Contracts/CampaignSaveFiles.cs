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

public sealed record CampaignGridPointFile(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record CampaignActiveInvasionStatusFile(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("strength")] int Strength,
    [property: JsonPropertyName("remaining_ticks")] int RemainingTicks);

public sealed record CampaignActiveInvasionUnitFile(
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("formation_index")] int FormationIndex,
    [property: JsonPropertyName("position")] CampaignGridPointFile Position,
    [property: JsonPropertyName("hp")] int Hp,
    [property: JsonPropertyName("shield")] int Shield,
    [property: JsonPropertyName("route_progress_units")] long RouteProgressUnits,
    [property: JsonPropertyName("path_index")] int PathIndex,
    [property: JsonPropertyName("move_remainder")] int MoveRemainder,
    [property: JsonPropertyName("next_move_tick")] int NextMoveTick,
    [property: JsonPropertyName("next_attack_tick")] int NextAttackTick,
    [property: JsonPropertyName("deployment_requested")] bool DeploymentRequested,
    [property: JsonPropertyName("admitted")] bool Admitted,
    [property: JsonPropertyName("target_entity_id")] string? TargetEntityId,
    [property: JsonPropertyName("archetype")] string Archetype,
    [property: JsonPropertyName("statuses")] IReadOnlyList<CampaignActiveInvasionStatusFile> Statuses);

public sealed record CampaignActiveInvasionGuardFile(
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("position")] CampaignGridPointFile Position,
    [property: JsonPropertyName("hp")] int Hp,
    [property: JsonPropertyName("next_move_tick")] int NextMoveTick,
    [property: JsonPropertyName("next_attack_tick")] int NextAttackTick,
    [property: JsonPropertyName("target_entity_id")] string? TargetEntityId,
    [property: JsonPropertyName("statuses")] IReadOnlyList<CampaignActiveInvasionStatusFile> Statuses);

public sealed record CampaignActiveInvasionCooldownFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ready_tick")] int ReadyTick);

public sealed record CampaignActiveInvasionSpellCooldownFile(
    [property: JsonPropertyName("spell_id")] string SpellId,
    [property: JsonPropertyName("remaining_ticks")] int RemainingTicks);

public sealed record CampaignActiveInvasionEventFile(
    [property: JsonPropertyName("tick")] int Tick,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("target_id")] string? TargetId,
    [property: JsonPropertyName("position")] CampaignGridPointFile? Position,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("source_position")] CampaignGridPointFile? SourcePosition,
    [property: JsonPropertyName("source_definition_id")] string? SourceDefinitionId);

public sealed record CampaignActiveInvasionFile(
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("floor_id")] string FloorId,
    [property: JsonPropertyName("map_digest")] string MapDigest,
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("tick")] int Tick,
    [property: JsonPropertyName("mp")] int Mp,
    [property: JsonPropertyName("used_deployment_capacity")] int UsedDeploymentCapacity,
    [property: JsonPropertyName("retreat_remaining_ticks")] int? RetreatRemainingTicks,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("secured_loot")] CampaignResourceFile SecuredLoot,
    [property: JsonPropertyName("objective_structure_hp")] int ObjectiveStructureHp,
    [property: JsonPropertyName("cleared_section_ids")] IReadOnlyList<string> ClearedSectionIds,
    [property: JsonPropertyName("units")] IReadOnlyList<CampaignActiveInvasionUnitFile> Units,
    [property: JsonPropertyName("guards")] IReadOnlyList<CampaignActiveInvasionGuardFile> Guards,
    [property: JsonPropertyName("trap_cooldowns")] IReadOnlyList<CampaignActiveInvasionCooldownFile> TrapCooldowns,
    [property: JsonPropertyName("facility_cooldowns")] IReadOnlyList<CampaignActiveInvasionCooldownFile> FacilityCooldowns,
    [property: JsonPropertyName("spell_cooldowns")] IReadOnlyList<CampaignActiveInvasionSpellCooldownFile> SpellCooldowns,
    [property: JsonPropertyName("events")] IReadOnlyList<CampaignActiveInvasionEventFile> Events,
    [property: JsonPropertyName("is_first_clear_scenario")] bool IsFirstClearScenario,
    [property: JsonPropertyName("is_resolved")] bool IsResolved);

public sealed record CampaignClearedDungeonFile(
    [property: JsonPropertyName("archive_id")] string ArchiveId,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("cleared_day")] int ClearedDay,
    [property: JsonPropertyName("final_assault_profile_id")] string FinalAssaultProfileId,
    [property: JsonPropertyName("dungeon")] PlayerDungeonSaveFile Dungeon);

public sealed record CampaignChallengeBestFile(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("best_score")] int BestScore);


public sealed record CampaignDiscoveryFile(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("id")] string Id);

public sealed record CampaignDefenseRecordFile(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("core_hp")] int CoreHp,
    [property: JsonPropertyName("core_max_hp")] int CoreMaxHp,
    [property: JsonPropertyName("deepest_floor_depth")] int DeepestFloorDepth);

public sealed record CampaignInvasionRecordFile(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("floor_id")] string FloorId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("granted_loot")] CampaignResourceFile GrantedLoot,
    [property: JsonPropertyName("first_clear")] bool FirstClear);

public sealed record CampaignChallengeRecordFile(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("archive_id")] string ArchiveId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("score")] int Score);


public sealed record CampaignEquippedCosmeticFile(
    [property: JsonPropertyName("target_key")] string TargetKey,
    [property: JsonPropertyName("product_id")] string ProductId);

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
    [property: JsonPropertyName("challenge_best_scores")] IReadOnlyList<CampaignChallengeBestFile>? ChallengeBestScores = null,
    [property: JsonPropertyName("seen_narrative_beat_ids")] IReadOnlyList<string>? SeenNarrativeBeatIds = null,
    [property: JsonPropertyName("discovery")] IReadOnlyList<CampaignDiscoveryFile>? Discovery = null,
    [property: JsonPropertyName("defense_records")] IReadOnlyList<CampaignDefenseRecordFile>? DefenseRecords = null,
    [property: JsonPropertyName("invasion_records")] IReadOnlyList<CampaignInvasionRecordFile>? InvasionRecords = null,
    [property: JsonPropertyName("challenge_records")] IReadOnlyList<CampaignChallengeRecordFile>? ChallengeRecords = null,
    [property: JsonPropertyName("owned_cosmetic_ids")] IReadOnlyList<string>? OwnedCosmeticIds = null,
    [property: JsonPropertyName("equipped_cosmetics")] IReadOnlyList<CampaignEquippedCosmeticFile>? EquippedCosmetics = null);
