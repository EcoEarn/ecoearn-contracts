using System.IO;
using AElf.Boilerplate.TestBase;
using AElf.Contracts.MultiToken;
using AElf.ContractTestBase.ContractTestKit;
using AElf.Cryptography.ECDSA;
using AElf.CSharp.Core;
using AElf.Kernel;
using AElf.Standards.ACS0;
using AElf.Types;
using EcoEarn.Contracts.Points;
using EcoEarn.Contracts.TestPointsContract;
using Google.Protobuf;
using Volo.Abp.Threading;

namespace EcoEarn.Contracts.Tokens;

public class EcoEarnTokensContractTestBase : DAppContractTestBase<EcoEarnTokensContractTestModule>
{
    internal ACS0Container.ACS0Stub ZeroContractStub { get; set; }
    internal TokenContractContainer.TokenContractStub TokenContractStub { get; set; }
    internal TokenContractContainer.TokenContractStub TokenContractUserStub { get; set; }
    internal Address PointsContractAddress { get; set; }
    internal Address EcoEarnTokensContractAddress { get; set; }
    internal Address EcoEarnPointsContractAddress { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractStub EcoEarnTokensContractStub { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractStub EcoEarnTokensContractUserStub { get; set; }
    internal EcoEarnTokensContractContainer.EcoEarnTokensContractStub EcoEarnTokensContractUser2Stub { get; set; }
    internal EcoEarnPointsContractContainer.EcoEarnPointsContractStub EcoEarnPointsContractStub { get; set; }

    internal TestPointsContractContainer.TestPointsContractStub PointsContractStub { get; set; }

    protected ECKeyPair DefaultKeyPair => Accounts[0].KeyPair;
    protected Address DefaultAddress => Accounts[0].Address;
    protected ECKeyPair UserKeyPair => Accounts[1].KeyPair;
    protected Address UserAddress => Accounts[1].Address;

    protected ECKeyPair User2KeyPair => Accounts[2].KeyPair;
    protected Address User2Address => Accounts[2].Address;

    protected readonly IBlockTimeProvider BlockTimeProvider;
    protected readonly IContractTestService ContractTestService;

    protected EcoEarnTokensContractTestBase()
    {
        BlockTimeProvider = GetRequiredService<IBlockTimeProvider>();
        ContractTestService = GetRequiredService<IContractTestService>();

        ZeroContractStub = GetContractStub<ACS0Container.ACS0Stub>(BasicContractZeroAddress, DefaultKeyPair);
        TokenContractStub =
            GetContractStub<TokenContractContainer.TokenContractStub>(TokenContractAddress, DefaultKeyPair);
        TokenContractUserStub =
            GetContractStub<TokenContractContainer.TokenContractStub>(TokenContractAddress, UserKeyPair);

        var result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(File.ReadAllBytes(typeof(EcoEarnTokensContract).Assembly.Location))
            }));

        EcoEarnTokensContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);

        EcoEarnTokensContractStub =
            GetContractStub<EcoEarnTokensContractContainer.EcoEarnTokensContractStub>(EcoEarnTokensContractAddress,
                DefaultKeyPair);
        EcoEarnTokensContractUserStub =
            GetContractStub<EcoEarnTokensContractContainer.EcoEarnTokensContractStub>(EcoEarnTokensContractAddress,
                UserKeyPair);
        EcoEarnTokensContractUser2Stub =
            GetContractStub<EcoEarnTokensContractContainer.EcoEarnTokensContractStub>(EcoEarnTokensContractAddress,
                User2KeyPair);

        result = AsyncHelper.RunSync(async () => await ZeroContractStub.DeploySmartContract.SendAsync(
            new ContractDeploymentInput
            {
                Category = KernelConstants.CodeCoverageRunnerCategory,
                Code = ByteString.CopyFrom(File.ReadAllBytes(typeof(EcoEarnPointsContract).Assembly.Location))
            }));
        EcoEarnPointsContractAddress = Address.Parser.ParseFrom(result.TransactionResult.ReturnValue);
        EcoEarnPointsContractStub =
            GetContractStub<EcoEarnPointsContractContainer.EcoEarnPointsContractStub>(EcoEarnPointsContractAddress,
                DefaultKeyPair);

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
    }

    internal T GetContractStub<T>(Address contractAddress, ECKeyPair senderKeyPair) where T : ContractStubBase, new()
    {
        return GetTester<T>(contractAddress, senderKeyPair);
    }
}