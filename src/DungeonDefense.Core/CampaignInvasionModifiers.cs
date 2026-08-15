namespace DungeonDefense.Core;

public readonly record struct CampaignInvasionModifier(int DeploymentCapacityBonus = 0)
{
    public CampaignInvasionModifier Add(CampaignInvasionModifier other)
        => new(checked(DeploymentCapacityBonus + other.DeploymentCapacityBonus));

    public CampaignInvasionModifier Validate()
    {
        if (DeploymentCapacityBonus < 0) throw new ArgumentOutOfRangeException(nameof(DeploymentCapacityBonus));
        return this;
    }
}
