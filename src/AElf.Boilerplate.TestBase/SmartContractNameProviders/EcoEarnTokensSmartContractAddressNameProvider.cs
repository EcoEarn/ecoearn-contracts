using AElf.Kernel.Infrastructure;
using AElf.Types;

namespace AElf.Boilerplate.TestBase.SmartContractNameProviders;

public class EcoEarnTokensSmartContractAddressNameProvider
{
    public static readonly Hash Name = HashHelper.ComputeFrom("EcoEarn.Contracts.Tokens");

    public static readonly string StringName = Name.ToStorageKey();
    public Hash ContractName => Name;
    public string ContractStringName => StringName;
}