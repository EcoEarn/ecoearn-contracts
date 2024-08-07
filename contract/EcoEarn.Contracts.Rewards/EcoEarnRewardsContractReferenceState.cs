using AElf.Contracts.MultiToken;
using AElf.Standards.ACS0;
using EcoEarn.Contracts.Points;
using EcoEarn.Contracts.Tokens;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractState
{
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
    internal TokenContractContainer.TokenContractReferenceState TokenContract { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractReferenceState EcoEarnPointsContract { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractReferenceState EcoEarnTokensContract { get; set; }
}