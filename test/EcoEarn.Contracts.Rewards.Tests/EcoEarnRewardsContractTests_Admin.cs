using System.Linq;
using System.Threading.Tasks;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests : EcoEarnRewardsContractTestBase
{
    [Fact]
    public async Task InitializeTests()
    {
        var input = new InitializeInput
        {
            Admin = UserAddress,
            EcoearnPointsContract = EcoEarnPointsContractAddress,
            EcoearnTokensContract = EcoEarnTokensContractAddress
        };

        var result = await EcoEarnRewardsContractStub.Initialize.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var admin = await EcoEarnRewardsContractStub.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(UserAddress);

        // initialize twice
        result = await EcoEarnRewardsContractStub.Initialize
            .SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Already initialized.");
    }

    [Fact]
    public async Task InitializeTests_Fail()
    {
        // empty address
        var result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = new Address(),
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput());
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn points contract.");

        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn points contract.");

        // sender != author
        result = await UserEcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = UserAddress,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetAdminTests()
    {
        await Initialize();

        var output = await EcoEarnRewardsContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(DefaultAddress);

        var result = await EcoEarnRewardsContractStub.SetAdmin.SendAsync(DefaultAddress);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(AdminSet)).ShouldBeNull();

        result = await EcoEarnRewardsContractStub.SetAdmin.SendAsync(UserAddress);
        var log = GetLogEvent<AdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnRewardsContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetAdminTests_Fail()
    {
        await Initialize();

        var result =
            await EcoEarnRewardsContractStub.SetAdmin.SendWithExceptionAsync(
                new Address());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await UserEcoEarnRewardsContractStub.SetAdmin.SendWithExceptionAsync(UserAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task Initialize()
    {
        await EcoEarnRewardsContractStub.Initialize.SendAsync(new InitializeInput
        {
            EcoearnPointsContract = EcoEarnPointsContractAddress,
            EcoearnTokensContract = EcoEarnTokensContractAddress
        });
    }
}