using AElf.Kernel.Infrastructure;
using AElf.Types;

namespace AElf.Boilerplate.TestBase.SmartContractNameProviders;

public class EcoEarnPointsSmartContractAddressNameProvider
{
    public static readonly Hash Name = HashHelper.ComputeFrom("EcoEarn.Contracts.Points");

    public static readonly string StringName = Name.ToStorageKey();
    public Hash ContractName => Name;
    public string ContractStringName => StringName;
}