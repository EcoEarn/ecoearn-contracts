using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace EcoEarn.Contracts.TestPointsContract;

public class TestPointsContractState : ContractState
{
    public SingletonState<Address> Admin { get; set; }
    public SingletonState<string> PointsName { get; set; }
}