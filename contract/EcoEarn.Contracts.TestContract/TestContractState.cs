using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace EcoEarn.Contracts.TestContract;

public class TestContractState : ContractState
{
    public MappedState<Hash, LiquidityInfo> LiquidityInfoMap { get; set; }
    
    internal TokenContractContainer.TokenContractReferenceState TokenContract { get; set; }

}