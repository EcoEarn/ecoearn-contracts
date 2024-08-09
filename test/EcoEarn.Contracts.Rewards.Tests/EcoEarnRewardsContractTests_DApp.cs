using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests
{
    private const string PointsName = "point";
    private const string Symbol = "SGR-1";
    private const string DefaultSymbol = "ELF";
    private readonly Hash _appId = HashHelper.ComputeFrom("dapp");

    [Fact]
    public async Task RegisterTests()
    {
        await Initialize();

        var result = await EcoEarnRewardsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            UpdateAddress = DefaultAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Registered>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.Admin.ShouldBe(DefaultAddress);

        var output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(_appId);
        output.DappId.ShouldBe(_appId);
        output.Admin.ShouldBe(DefaultAddress);
        output.Config.UpdateAddress.ShouldBe(DefaultAddress);

        output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(new Hash());
        output.DappId.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterTests_Fail()
    {
        var result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput());
        result.TransactionResult.Error.ShouldContain("Not initialized.");

        await Initialize();

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Admin = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            UpdateAddress = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            UpdateAddress = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Dapp id not exists.");

        result = await UserEcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = UserAddress,
            UpdateAddress = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("No permission to register.");

        await EcoEarnRewardsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress,
            UpdateAddress = DefaultAddress
        });

        result = await EcoEarnRewardsContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress,
            UpdateAddress = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Dapp registered.");
    }

    [Fact]
    public async Task SetDappAdminTests()
    {
        await Initialize();
        await Register();

        var output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(DefaultAddress);

        var result = await EcoEarnRewardsContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(DappAdminSet)).ShouldBeNull();

        result = await EcoEarnRewardsContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        var log = GetLogEvent<DappAdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetDappAdminTests_Fail()
    {
        await Initialize();
        await Register();

        var result = await EcoEarnRewardsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Dapp not exists.");

        result = await EcoEarnRewardsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await UserEcoEarnRewardsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetDappConfigTests()
    {
        await Initialize();
        await Register();

        var output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(_appId);
        output.Config.UpdateAddress.ShouldBe(DefaultAddress);

        var result = await EcoEarnRewardsContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                UpdateAddress = DefaultAddress
            }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(DappConfigSet)).ShouldBeNull();

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                UpdateAddress = UserAddress
            }
        });
        var log = GetLogEvent<DappConfigSet>(result.TransactionResult);
        log.Config.UpdateAddress.ShouldBe(UserAddress);
        output = await EcoEarnRewardsContractStub.GetDappInfo.CallAsync(_appId);
        output.Config.UpdateAddress.ShouldBe(UserAddress);
    }
    
    [Fact]
    public async Task SetDappConfigTests_Fail()
    {
        await Initialize();
        await Register();

        var result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig()
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig
            {
                UpdateAddress = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig
            {
                UpdateAddress = UserAddress
            }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        result = await UserEcoEarnRewardsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                UpdateAddress = UserAddress
            }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task Register()
    {
        await EcoEarnRewardsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress,
            UpdateAddress = DefaultAddress
        });
    }

    private async Task CreateToken()
    {
        await TokenContractStub.Create.SendAsync(new CreateInput
        {
            Symbol = "SEED-0",
            TokenName = "SEED-0 token",
            TotalSupply = 1,
            Decimals = 0,
            Issuer = DefaultAddress,
            IsBurnable = true,
            IssueChainId = 0,
        });

        var seedOwnedSymbol = "SGR" + "-0";
        var seedExpTime = BlockTimeProvider.GetBlockTime().AddDays(365);
        await TokenContractStub.Create.SendAsync(new CreateInput
        {
            Symbol = "SEED-1",
            TokenName = "SEED-1 token",
            TotalSupply = 1,
            Decimals = 0,
            Issuer = DefaultAddress,
            IsBurnable = true,
            IssueChainId = 0,
            LockWhiteList = { TokenContractAddress },
            ExternalInfo = new ExternalInfo()
            {
                Value =
                {
                    {
                        "__seed_owned_symbol",
                        seedOwnedSymbol
                    },
                    {
                        "__seed_exp_time",
                        seedExpTime.Seconds.ToString()
                    }
                }
            }
        });

        await TokenContractStub.Issue.SendAsync(new IssueInput
        {
            Symbol = "SEED-1",
            Amount = 1,
            To = DefaultAddress,
            Memo = ""
        });
        await TokenContractStub.Create.SendAsync(new CreateInput
        {
            Symbol = "SGR-0",
            TokenName = "SGR-0 token",
            TotalSupply = 1,
            Decimals = 0,
            Issuer = DefaultAddress,
            IsBurnable = true,
            IssueChainId = 0,
            LockWhiteList = { TokenContractAddress }
        });
        await TokenContractStub.Create.SendAsync(new CreateInput
        {
            Symbol = "SGR-1",
            TokenName = "SGR-1 token",
            TotalSupply = 1000000000_00000000,
            Decimals = 0,
            Issuer = DefaultAddress,
            IsBurnable = true,
            IssueChainId = 0,
            LockWhiteList = { TokenContractAddress }
        });
        await TokenContractStub.Issue.SendAsync(new IssueInput
        {
            Amount = 1000000000_00000000,
            Symbol = Symbol,
            To = DefaultAddress
        });
    }
}