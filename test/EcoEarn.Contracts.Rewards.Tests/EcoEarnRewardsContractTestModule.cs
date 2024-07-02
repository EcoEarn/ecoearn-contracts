using AElf.Boilerplate.TestBase;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using EcoEarn.Contracts.Rewards.ContractInitializationProvider;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace EcoEarn.Contracts.Rewards;

[DependsOn(typeof(MainChainDAppContractTestModule))]
public class EcoEarnRewardsContractTestModule : MainChainDAppContractTestModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IContractInitializationProvider, EcoEarnRewardsContractInitializationProvider>();
        Configure<ContractOptions>(o => o.ContractDeploymentAuthorityRequired = false);
    }
}