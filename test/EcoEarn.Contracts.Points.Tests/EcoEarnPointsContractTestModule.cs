using AElf.Boilerplate.TestBase;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using EcoEarn.Contracts.Points.ContractInitializationProvider;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace EcoEarn.Contracts.Points;

[DependsOn(typeof(MainChainDAppContractTestModule))]
public class EcoEarnPointsContractTestModule : MainChainDAppContractTestModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IContractInitializationProvider, EcoEarnPointsContractInitializationProvider>();
        Configure<ContractOptions>(o => o.ContractDeploymentAuthorityRequired = false);
    }
}