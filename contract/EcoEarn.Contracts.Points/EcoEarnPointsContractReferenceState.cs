using AElf.Contracts.MultiToken;
using AElf.Standards.ACS0;
using EcoEarn.Contracts.Tokens;
using Points.Contracts.PointsContract;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractState
{
    internal ACS0Container.ACS0ReferenceState GenesisContract { get; set; }
    internal TokenContractContainer.TokenContractReferenceState TokenContract { get; set; }
    internal PointsContractContainer.PointsContractReferenceState PointsContract { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractReferenceState EcoEarnTokensContract { get; set; }
}