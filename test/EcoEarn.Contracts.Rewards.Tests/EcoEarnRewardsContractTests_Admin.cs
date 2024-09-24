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
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            PointsContract = PointsContractAddress,
            UpdateAddress = DefaultAddress
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
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn tokens contract.");
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn tokens contract.");
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            PointsContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            PointsContract = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");
        
        result = await EcoEarnRewardsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            PointsContract = DefaultAddress,
            UpdateAddress = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

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
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            PointsContract = PointsContractAddress,
            UpdateAddress = DefaultAddress
        });
        await EcoEarnPointsContractStub.Initialize.SendAsync(new Points.InitializeInput
        {
            PointsContract = PointsContractAddress,
            CommissionRate = 1000,
            Recipient = User2Address,
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress,
            UpdateAddress = DefaultAddress
        });
        await EcoEarnTokensContractStub.Initialize.SendAsync(new Tokens.InitializeInput
        {
            CommissionRate = 100,
            Recipient = User2Address,
            EcoearnPointsContract = EcoEarnPointsContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress,
            IsRegisterRestricted = true,
            MaximumPositionCount = 100
        });
        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
        
        await EcoEarnRewardsContractStub.SetPointsContractConfig.SendAsync(new SetPointsContractConfigInput
        {
            Admin = DefaultAddress,
            DappId = _appIdEcoEarn
        });
        
        await EcoEarnPointsContractStub.Register.SendAsync(new Points.RegisterInput
        {
            DappId = _appId
        });
        await EcoEarnTokensContractStub.Register.SendAsync(new Tokens.RegisterInput
        {
            DappId = _appId
        });
    }
}