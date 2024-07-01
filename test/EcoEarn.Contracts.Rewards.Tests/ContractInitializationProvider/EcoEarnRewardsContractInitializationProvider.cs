using System.Collections.Generic;
using AElf.Boilerplate.TestBase.SmartContractNameProviders;
using AElf.Kernel.SmartContract.Application;
using AElf.Types;
using Volo.Abp.DependencyInjection;

namespace EcoEarn.Contracts.Rewards.ContractInitializationProvider;

public class EcoEarnRewardsContractInitializationProvider : IContractInitializationProvider, ISingletonDependency
{
    public List<ContractInitializationMethodCall> GetInitializeMethodList(byte[] contractCode)
    {
        return new List<ContractInitializationMethodCall>();
    }

    public Hash SystemSmartContractName => EcoEarnRewardsSmartContractAddressNameProvider.Name;
    public string ContractCodeName => "EcoEarnRewardsContract";
}