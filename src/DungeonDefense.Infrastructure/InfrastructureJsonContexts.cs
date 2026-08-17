using System.Text.Json.Serialization;
using DungeonDefense.Contracts;

namespace DungeonDefense.Infrastructure;

[JsonSerializable(typeof(VerticalSliceContentLoader.DefenseContentDto))]
internal sealed partial class VerticalSliceJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(DefenseAssaultProfileLoader.ProfileFileDto))]
internal sealed partial class AssaultProfileJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(CampaignProgressionContentLoader.CampaignProgressionFile))]
internal sealed partial class CampaignProgressionJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(RegionCampaignContentLoader.RegionCampaignFile))]
internal sealed partial class RegionCampaignJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(RegionDefenseScheduleContentLoader.RegionDefenseScheduleFile))]
internal sealed partial class RegionDefenseScheduleJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InvasionContentLoader.InvasionContentFile))]
internal sealed partial class InvasionContentJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InvasionSpatialMapLoader.InvasionSpatialMapFile))]
internal sealed partial class InvasionSpatialMapJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(DungeonBlueprintFile))]
[JsonSerializable(typeof(DungeonBuildPatternFile))]
internal sealed partial class DungeonStaticJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(SemanticCommandSequenceFile))]
internal sealed partial class SemanticCommandJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(PlayerDungeonSaveFile))]
internal sealed partial class PlayerDungeonSaveJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(CampaignSaveFile))]
internal sealed partial class CampaignSaveJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(NarrativeContentFile))]
internal sealed partial class NarrativeContentJsonContext : JsonSerializerContext;
