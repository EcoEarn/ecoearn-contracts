using System.IO;
using AElf.Boilerplate.TestBase;
using AElf.Contracts.MultiToken;
using AElf.ContractTestBase.ContractTestKit;
using AElf.Cryptography.ECDSA;
using AElf.CSharp.Core;
using AElf.Kernel;
using AElf.Standards.ACS0;
using AElf.Types;
using EcoEarn.Contracts.Rewards;
using EcoEarn.Contracts.TestPointsContract;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf;
using Volo.Abp.Threading;

namespace EcoEarn.Contracts.Points;

public class EcoEarnPointsContractTestBase : DAppContractTestBase<EcoEarnPointsContractTestModule>
{
    internal ACS0Container.ACS0Stub ZeroContractStub { get; set; }
    internal TokenContractContainer.TokenContractStub TokenContractStub { get; set; }
    internal Address PointsContractAddress { get; set; }
    internal Address EcoEarnPointsContractAddress { get; set; }
    internal Address EcoEarnTokensContractAddress { get; set; }
    internal Address EcoEarnRewardsContractAddress { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractStub EcoEarnPointsContractStub { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractStub UserEcoEarnPointsContractStub { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractStub User2EcoEarnPointsContractStub { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractStub EcoEarnTokensContractStub { get; set; }
    internal EcoEarnRewardsContractContainer.EcoEarnRewardsContractStub EcoEarnRewardsContractStub { get; set; }
    internal TestPointsContractContainer.TestPointsContractStub PointsContractStub { get; set; }

    protected ECKeyPair DefaultKeyPair => Accounts[0].KeyPair;
    protected Address DefaultAddress => Accounts[0].Address;
    protected ECKeyPair UserKeyPair => Accounts[1].KeyPair;
    protected Address UserAddress => Accounts[1].Address;

    protected ECKeyPair User2KeyPair => Accounts[2].KeyPair;
    protected Address User2Address => Accounts[2].Address;

    protected readonly IBlockTimeProvider BlockTimeProvider;

    protected EcoEarnPointsContractTestBase()
    {
        BlockTimeProvider = GetRequiredService<IBlockTimeProvider>();

        ZeroContractStub = GetContractStub<ACS0Container.ACS0Stub>(BasicContractZeroAddress, DefaultKeyPair);
        TokenContractStub =
            GetContractStub<TokenContractContainer.TokenContractStub>(TokenContractAddress, DefaultKeyPair);

        var result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(File.ReadAllBytes(typeof(EcoEarnPointsContract).Assembly.Location))
            }));

        EcoEarnPointsContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);

        EcoEarnPointsContractStub =
            GetContractStub<EcoEarnPointsContractContainer.EcoEarnPointsContractStub>(EcoEarnPointsContractAddress,
                DefaultKeyPair);
        UserEcoEarnPointsContractStub =
            GetContractStub<EcoEarnPointsContractContainer.EcoEarnPointsContractStub>(EcoEarnPointsContractAddress,
                UserKeyPair);
        User2EcoEarnPointsContractStub =
            GetContractStub<EcoEarnPointsContractContainer.EcoEarnPointsContractStub>(EcoEarnPointsContractAddress,
                User2KeyPair);

        result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(
                    File.ReadAllBytes(typeof(TestPointsContract.TestPointsContract).Assembly.Location))
            }));

        PointsContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
        PointsContractStub =
            GetContractStub<TestPointsContractContainer.TestPointsContractStub>(PointsContractAddress, DefaultKeyPair);

        result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(
                    File.ReadAllBytes(typeof(EcoEarnTokensContract).Assembly.Location))
            }));

        EcoEarnTokensContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
        EcoEarnTokensContractStub =
            GetContractStub<EcoEarnTokensContractContainer.EcoEarnTokensContractStub>(EcoEarnTokensContractAddress,
                DefaultKeyPair);

        result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(File.ReadAllBytes(typeof(EcoEarnRewardsContract).Assembly.Location))
            }));
        EcoEarnRewardsContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
        EcoEarnRewardsContractStub =
            GetContractStub<EcoEarnRewardsContractContainer.EcoEarnRewardsContractStub>(EcoEarnRewardsContractAddress,
                DefaultKeyPair);
    }

    internal T GetContractStub<T>(Address contractAddress, ECKeyPair senderKeyPair) where T : ContractStubBase, new()
    {
        return GetTester<T>(contractAddress, senderKeyPair);
    }
}