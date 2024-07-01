using System.Linq;
using System.Threading.Tasks;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests : EcoEarnPointsContractTestBase
{
    [Fact]
    public async Task InitializeTests()
    {
        var input = new InitializeInput
        {
            PointsContract = PointsContractAddress,
            CommissionRate = 100,
            Recipient = User2Address,
            Admin = UserAddress,
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress
        };

        var result = await EcoEarnPointsContractStub.Initialize.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var admin = await EcoEarnPointsContractStub.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(UserAddress);

        var config = await EcoEarnPointsContractStub.GetConfig.CallAsync(new Empty());
        config.CommissionRate.ShouldBe(100);
        config.Recipient.ShouldBe(User2Address);

        // initialize twice
        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Already initialized.");
    }

    [Fact]
    public async Task InitializeTests_DefaultAddress()
    {
        var input = new InitializeInput
        {
            PointsContract = PointsContractAddress,
            CommissionRate = 100,
            EcoearnTokensContract = DefaultAddress,
            EcoearnRewardsContract = DefaultAddress
        };

        var result = await EcoEarnPointsContractStub.Initialize.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var admin = await EcoEarnPointsContractStub.GetAdmin.CallAsync(new Empty());
        admin.ShouldBe(DefaultAddress);

        var config = await EcoEarnPointsContractStub.GetConfig.CallAsync(new Empty());
        config.Recipient.ShouldBe(DefaultAddress);
    }

    [Fact]
    public async Task InitializeTests_Fail()
    {
        // empty address
        var result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = new Address(),
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput());
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn tokens contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress,
            EcoearnTokensContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn tokens contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn rewards contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            EcoearnRewardsContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid ecoearn rewards contract.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            EcoearnRewardsContract = DefaultAddress,
            CommissionRate = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid commission rate.");

        result = await EcoEarnPointsContractStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            PointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            EcoearnRewardsContract = DefaultAddress,
            CommissionRate = 0,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        // sender != author
        result = await EcoEarnPointsContractUserStub.Initialize.SendWithExceptionAsync(new InitializeInput
        {
            Admin = UserAddress,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetAdminTests()
    {
        await Initialize();

        var output = await EcoEarnPointsContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(DefaultAddress);

        var result = await EcoEarnPointsContractStub.SetAdmin.SendAsync(DefaultAddress);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(AdminSet)).ShouldBeNull();

        result = await EcoEarnPointsContractStub.SetAdmin.SendAsync(UserAddress);
        var log = GetLogEvent<AdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnPointsContractStub.GetAdmin.CallAsync(new Empty());
        output.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetAdminTests_Fail()
    {
        await Initialize();

        var result = await EcoEarnPointsContractStub.SetAdmin.SendWithExceptionAsync(new Address());
        result.TransactionResult.Error.ShouldContain("Invalid input.");

        result = await EcoEarnPointsContractUserStub.SetAdmin.SendWithExceptionAsync(UserAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetConfigTests()
    {
        await Initialize();

        var config = await EcoEarnPointsContractStub.GetConfig.CallAsync(new Empty());
        config.CommissionRate.ShouldBe(1000);
        config.Recipient.ShouldBe(User2Address);

        var input = new Config
        {
            CommissionRate = 50,
            Recipient = DefaultAddress
        };
        var result = await EcoEarnPointsContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<ConfigSet>(result.TransactionResult);
        log.Config.ShouldBe(input);

        config = await EcoEarnPointsContractStub.GetConfig.CallAsync(new Empty());
        config.ShouldBe(input);

        result = await EcoEarnPointsContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(ConfigSet)).ShouldBeNull();

        input.CommissionRate = 500;
        result = await EcoEarnPointsContractStub.SetConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        log = GetLogEvent<ConfigSet>(result.TransactionResult);
        log.Config.CommissionRate.ShouldBe(500);
        log.Config.Recipient.ShouldBe(DefaultAddress);
    }

    [Fact]
    public async Task SetConfigTests_Fail()
    {
        {
            var result = await EcoEarnPointsContractStub.SetConfig.SendWithExceptionAsync(new Config());
            result.TransactionResult.Error.ShouldContain("No permission.");
        }

        await Initialize();

        {
            var result = await EcoEarnPointsContractUserStub.SetConfig.SendWithExceptionAsync(new Config());
            result.TransactionResult.Error.ShouldContain("No permission.");
        }
        {
            var result = await EcoEarnPointsContractStub.SetConfig.SendWithExceptionAsync(new Config
            {
                CommissionRate = -1
            });
            result.TransactionResult.Error.ShouldContain("Invalid commission rate.");
        }
        {
            var result = await EcoEarnPointsContractStub.SetConfig.SendWithExceptionAsync(new Config
            {
                CommissionRate = 50,
                Recipient = new Address()
            });
            result.TransactionResult.Error.ShouldContain("Invalid recipient.");
        }
    }

    private async Task Initialize()
    {
        await EcoEarnPointsContractStub.Initialize.SendAsync(new InitializeInput
        {
            PointsContract = PointsContractAddress,
            CommissionRate = 1000,
            Recipient = User2Address,
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress
        });
        await EcoEarnRewardsContractStub.Initialize.SendAsync(new Rewards.InitializeInput
        {
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            EcoearnPointsContract = EcoEarnPointsContractAddress
        });
        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
    }
}