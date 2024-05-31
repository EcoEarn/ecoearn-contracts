using System.Linq;
using System.Threading.Tasks;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests : EcoEarnTokensContractTestBase
{
    [Fact]
    public async Task InitializeTests()
    {
        var input = new InitializeInput
        {
            CommissionRate = 100,
            Recipient = User2Address,
            Admin = UserAddress,
            EcoearnPointsContract = DefaultAddress
        };

        var result = await EcoEarnTokensContractStub.Initialize.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(UserAddress);

        var config = await EcoEarnTokensContractStub.GetConfig.CallAsync(new Empty());
        config.CommissionRate.ShouldBe(100);
        config.Recipient.ShouldBe(User2Address);

        // initialize twice
        result = await EcoEarnTokensContractStub.Initialize
            .SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Already initialized.");
    }

    [Fact]
    public async Task InitializeTests_DefaultAddress()
    {
        var input = new InitializeInput
        {
            CommissionRate = 100,
            EcoearnPointsContract = DefaultAddress
        };

        var result = await EcoEarnTokensContractStub.Initialize.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(DefaultAddress);

        var config = await EcoEarnTokensContractStub.GetConfig.CallAsync(new Empty());
        config.Recipient.ShouldBe(DefaultAddress);
    }

    [Fact]
    public async Task InitializeTests_Fail()
    {
        // empty address
        var result = await EcoEarnTokensContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = new Address(),
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnTokensContractStub.Initialize.SendWithExceptionAsync(new InitializeInput());
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn points contract.");

        result = await EcoEarnTokensContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn points contract.");

        result = await EcoEarnTokensContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            CommissionRate = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid commission rate.");

        result = await EcoEarnTokensContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            CommissionRate = 0,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        // sender != author
        result = await EcoEarnTokensContractUserStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = UserAddress,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetAdminTests()
    {
        await Initialize();

        var output = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(DefaultAddress);

        var result = await EcoEarnTokensContractStub.SetAdmin.SendAsync(DefaultAddress);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(AdminSet)).ShouldBeNull();

        result = await EcoEarnTokensContractStub.SetAdmin.SendAsync(UserAddress);
        var log = GetLogEvent<AdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetAdminTests_Fail()
    {
        await Initialize();

        var result =
            await EcoEarnTokensContractStub.SetAdmin.SendWithExceptionAsync(
                new Address());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnTokensContractUserStub.SetAdmin.SendWithExceptionAsync(UserAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetConfigTests()
    {
        await Initialize();

        var config = await EcoEarnTokensContractStub.GetConfig.CallAsync(new Empty());
        config.CommissionRate.ShouldBe(100);
        config.Recipient.ShouldBe(User2Address);

        var input = new Config
        {
            CommissionRate = 50,
            Recipient = DefaultAddress,
            IsRegisterRestricted = false
        };
        var result = await EcoEarnTokensContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<ConfigSet>(result.TransactionResult);
        log.Config.ShouldBe(input);

        config = await EcoEarnTokensContractStub.GetConfig.CallAsync(new Empty());
        config.ShouldBe(input);

        result = await EcoEarnTokensContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(ConfigSet)).ShouldBeNull();

        input.CommissionRate = 500;
        result = await EcoEarnTokensContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        log = GetLogEvent<ConfigSet>(result.TransactionResult);
        log.Config.CommissionRate.ShouldBe(500);
        log.Config.Recipient.ShouldBe(DefaultAddress);
        log.Config.IsRegisterRestricted.ShouldBeFalse();
    }

    [Fact]
    public async Task SetConfigTests_Fail()
    {
        {
            var result =
                await EcoEarnTokensContractStub.SetConfig.SendWithExceptionAsync(
                    new Config());
            result.TransactionResult.Error.ShouldContain("No permission.");
        }

        await Initialize();

        {
            var result = await EcoEarnTokensContractUserStub.SetConfig.SendWithExceptionAsync(new Config());
            result.TransactionResult.Error.ShouldContain("No permission.");
        }
        {
            var result =
                await EcoEarnTokensContractStub.SetConfig.SendWithExceptionAsync(
                    new Config
                    {
                        CommissionRate = -1
                    });
            result.TransactionResult.Error.ShouldContain("Invalid commission rate.");
        }
        {
            var result =
                await EcoEarnTokensContractStub.SetConfig.SendWithExceptionAsync(
                    new Config
                    {
                        CommissionRate = 50,
                        Recipient = new Address()
                    });
            result.TransactionResult.Error.ShouldContain("Invalid recipient.");
        }
    }

    private async Task Initialize()
    {
        await EcoEarnTokensContractStub.Initialize.SendAsync(new InitializeInput
        {
            CommissionRate = 100,
            Recipient = User2Address,
            EcoearnPointsContract = EcoEarnPointsContractAddress,
            IsRegisterRestricted = true
        });
    }
}