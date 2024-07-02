using AElf.Boilerplate.TestBase;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using EcoEarn.Contracts.Tokens.ContractInitializationProvider;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace EcoEarn.Contracts.Tokens;

[DependsOn(typeof(MainChainDAppContractTestModule))]
public class EcoEarnTokensContractTestModule : MainChainDAppContractTestModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IContractInitializationProvider, EcoEarnTokensContractInitializationProvider>();
        Configure<ContractOptions>(o => o.ContractDeploymentAuthorityRequired = false);
    }
}