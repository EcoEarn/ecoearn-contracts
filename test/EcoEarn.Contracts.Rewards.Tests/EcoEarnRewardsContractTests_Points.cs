using System.Linq;
using System.Threading.Tasks;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests
{
    [Fact]
    public async Task SetPointsContractConfigTests()
    {
        await InitializeWithoutRegister();
        
        var result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendAsync(new SetPointsContractConfigInput
        {
            Admin = DefaultAddress,
            DappId = _appIdEcoEarn,
            PointsContract = PointsContractAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsContractConfigSet>(result.TransactionResult);
        log.PointsContract.ShouldBe(PointsContractAddress);
        log.Config.Admin.ShouldBe(DefaultAddress);
        log.Config.DappId.ShouldBe(_appIdEcoEarn);
        
        var output = await EcoEarnRewardsContractStub.GetPointsContractConfig.CallAsync(new Empty());
        output.PointsContract.ShouldBe(PointsContractAddress);
        output.Config.ShouldBe(log.Config);
        
        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendAsync(new SetPointsContractConfigInput
        {
            Admin = DefaultAddress,
            DappId = _appIdEcoEarn,
            PointsContract = PointsContractAddress
        });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(PointsContractConfigSet)).ShouldBeNull();
    }

    [Fact]
    public async Task SetRewardsContractConfigTests_Fail()
    {
        await Initialize();

        var result =
            await UserEcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
                new SetPointsContractConfigInput());
        result.TransactionResult.Error.ShouldContain("No permission.");

        result =
            await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
                new SetPointsContractConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");

        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
            new SetPointsContractConfigInput
            {
                PointsContract = new Address()
            });
        result.TransactionResult.Error.ShouldContain("Invalid points contract.");
        
        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
            new SetPointsContractConfigInput
            {
                PointsContract = DefaultAddress
            });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");
        
        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
            new SetPointsContractConfigInput
            {
                PointsContract = DefaultAddress,
                DappId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");
        
        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
            new SetPointsContractConfigInput
            {
                PointsContract = DefaultAddress,
                DappId = _appIdEcoEarn
            });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");
        
        result = await EcoEarnRewardsContractStub.SetPointsContractConfig.SendWithExceptionAsync(
            new SetPointsContractConfigInput
            {
                PointsContract = DefaultAddress,
                DappId = _appIdEcoEarn,
                Admin = new Address()
            });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");
    }
    
    [Fact]
    public async Task JoinTests()
    {
        await Initialize();
        
        var output = await EcoEarnRewardsContractStub.GetJoinRecord.CallAsync(UserAddress);
        output.Value.ShouldBe(false);

        var result = await UserEcoEarnRewardsContractStub.Join.SendAsync(new Empty());
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Joined>(result.TransactionResult);
        log.Domain.ShouldBe("domain");
        log.Registrant.ShouldBe(UserAddress);

        output = await EcoEarnRewardsContractStub.GetJoinRecord.CallAsync(UserAddress);
        output.Value.ShouldBe(true);
    }

    [Fact]
    public async Task JoinTests_Fail()
    {
        await InitializeWithoutRegister();
        
        var result = await EcoEarnRewardsContractStub.Join.SendWithExceptionAsync(new Empty());
        result.TransactionResult.Error.ShouldContain("Points contract config not set.");
        
        await EcoEarnRewardsContractStub.SetPointsContractConfig.SendAsync(new SetPointsContractConfigInput
        {
            Admin = DefaultAddress,
            DappId = _appIdEcoEarn,
            PointsContract = PointsContractAddress
        });
        
        await EcoEarnRewardsContractStub.Join.SendAsync(new Empty());
        
        result = await EcoEarnRewardsContractStub.Join.SendWithExceptionAsync(new Empty());
        result.TransactionResult.Error.ShouldContain("Already joined.");
    }

    [Fact]
    public async Task JoinForTests_Fail()
    {
        var result = await EcoEarnRewardsContractStub.JoinFor.SendWithExceptionAsync(DefaultAddress);
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task AcceptReferralTests()
    {
        await Initialize();

        var result = await UserEcoEarnRewardsContractStub.AcceptReferral.SendAsync(new AcceptReferralInput
        {
            Referrer = DefaultAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<ReferralAccepted>(result.TransactionResult);
        log.Referrer.ShouldBe(DefaultAddress);
        log.Invitee.ShouldBe(UserAddress);

        var output = await EcoEarnRewardsContractStub.GetJoinRecord.CallAsync(UserAddress);
        output.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task AcceptReferralTests_Fail()
    {
        var result = await UserEcoEarnRewardsContractStub.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput());
        result.TransactionResult.Error.ShouldContain("Invalid referrer.");
        
        result = await UserEcoEarnRewardsContractStub.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput
        {
            Referrer = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid referrer.");
        
        result = await UserEcoEarnRewardsContractStub.AcceptReferral.SendWithExceptionAsync(new AcceptReferralInput
        {
            Referrer = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid referrer.");
    }

    [Fact]
    public async Task BatchSettleTests()
    {
        await Initialize();
        
        var result = await EcoEarnRewardsContractStub.BatchSettle.SendAsync(new BatchSettleInput
        {
            ActionName = "action",
            UserPointsList = { new UserPoints
            {
                UserAddress = DefaultAddress,
                UserPointsValue = new BigIntValue
                {
                    Value = "100000000"
                }
            } }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
    }

    [Fact]
    public async Task BatchSettleTests_Fail()
    {
        var result = await EcoEarnRewardsContractStub.BatchSettle.SendWithExceptionAsync(new BatchSettleInput());
        result.TransactionResult.Error.ShouldContain("Invalid action name.");
        
        result = await EcoEarnRewardsContractStub.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            ActionName = "action"
        });
        result.TransactionResult.Error.ShouldContain("Invalid user points list.");
        
        result = await EcoEarnRewardsContractStub.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            ActionName = "action",
            UserPointsList = {  }
        });
        result.TransactionResult.Error.ShouldContain("Invalid user points list.");
        
        result = await EcoEarnRewardsContractStub.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            ActionName = "action",
            UserPointsList = { new UserPoints() }
        });
        result.TransactionResult.Error.ShouldContain("Points contract config not set.");

        await Initialize();
        
        result = await UserEcoEarnRewardsContractStub.BatchSettle.SendWithExceptionAsync(new BatchSettleInput
        {
            ActionName = "action",
            UserPointsList = { new UserPoints() }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task InitializeWithoutRegister()
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
    }
}