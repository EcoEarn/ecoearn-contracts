using System.Collections.Generic;
using AElf.Boilerplate.TestBase.SmartContractNameProviders;
using AElf.Kernel.SmartContract.Application;
using AElf.Types;
using Volo.Abp.DependencyInjection;

namespace EcoEarn.Contracts.Points.ContractInitializationProvider;

public class TestPointsContractInitializationProvider : IContractInitializationProvider, ISingletonDependency
{
    public List<ContractInitializationMethodCall> GetInitializeMethodList(byte[] contractCode)
    {
        return new List<ContractInitializationMethodCall>();
    }

    public Hash SystemSmartContractName => TestPointsContractSmartContractAddressNameProvider.Name;
    public string ContractCodeName => "TestPointsContract";
}