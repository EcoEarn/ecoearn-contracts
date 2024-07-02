using AElf.Contracts.MultiToken;
using AElf.Standards.ACS0;
using EcoEarn.Contracts.Points;
using EcoEarn.Contracts.Rewards;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractState
{
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
    internal TokenContractContainer.TokenContractReferenceState TokenContract { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractReferenceState EcoEarnPointsContract { get; set; }
    internal EcoEarnRewardsContractContainer.EcoEarnRewardsContractReferenceState EcoEarnRewardsContract { get; set; }
}