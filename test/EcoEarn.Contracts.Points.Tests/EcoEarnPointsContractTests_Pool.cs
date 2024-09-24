using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests
{
    private const string PointsName = "point";
    private const string Symbol = "SGR-1";
    private const string DefaultSymbol = "ELF";
    private readonly Hash _appId = HashHelper.ComputeFrom("dapp");

    [Fact]
    public async Task RegisterTests()
    {
        await Initialize();

        var result = await EcoEarnPointsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Registered>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.Admin.ShouldBe(DefaultAddress);
        log.Config.UpdateAddress.ShouldBe(DefaultAddress);

        var output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.DappId.ShouldBe(_appId);
        output.Admin.ShouldBe(DefaultAddress);
        output.Config.UpdateAddress.ShouldBe(DefaultAddress);

        output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(new Hash());
        output.DappId.ShouldBeNull();
    }

    [Fact]
    public async Task RegisterTests_Fail()
    {
        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput());
            result.TransactionResult.Error.ShouldContain("Not initialized.");
        }

        await Initialize();

        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput());
            result.TransactionResult.Error.ShouldContain("Invalid dapp id.");
        }
        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput
            {
                DappId = new Hash()
            });
            result.TransactionResult.Error.ShouldContain("Invalid dapp id.");
        }
        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput
            {
                DappId = HashHelper.ComputeFrom("test"),
                Admin = new Address()
            });
            result.TransactionResult.Error.ShouldContain("Invalid admin.");
        }
        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput
            {
                DappId = HashHelper.ComputeFrom("test")
            });
            result.TransactionResult.Error.ShouldContain("Dapp not exists.");
        }

        {
            var result = await UserEcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput
            {
                DappId = _appId,
                Admin = UserAddress
            });
            result.TransactionResult.Error.ShouldContain("No permission to register.");
        }

        await EcoEarnPointsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });

        {
            var result = await EcoEarnPointsContractStub.Register.SendWithExceptionAsync(new RegisterInput
            {
                DappId = _appId,
                Admin = DefaultAddress
            });
            result.TransactionResult.Error.ShouldContain("Dapp registered.");
        }
    }

    [Fact]
    public async Task SetDappAdminTests()
    {
        await Initialize();

        await Register();

        var output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(DefaultAddress);

        var result = await EcoEarnPointsContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(DappAdminSet)).ShouldBeNull();

        result = await EcoEarnPointsContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        var log = GetLogEvent<DappAdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetDappAdminTests_Fail()
    {
        await Initialize();

        await Register();

        var result = await EcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Admin = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Admin = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Dapp not exists.");

        result = await UserEcoEarnPointsContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task CreatePointsPoolTests()
    {
        await Initialize();

        await Register();
        await CreateToken();

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnPointsContractAddress,
            Amount = 1000,
            Symbol = Symbol
        });

        var blockTime = BlockTimeProvider.GetBlockTime();

        var input = new CreatePointsPoolInput
        {
            DappId = _appId,
            PointsName = PointsName,
            StartTime = blockTime.Seconds,
            EndTime = blockTime.Seconds + 100,
            RewardPerSecond = 10,
            RewardToken = Symbol,
            ReleasePeriods = { 10, 20, 30 },
            ClaimInterval = 10
        };
        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolCreated>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.PointsName.ShouldBe(PointsName);
        log.Config.RewardToken.ShouldBe(Symbol);
        log.Config.StartTime.Seconds.ShouldBe(blockTime.Seconds);
        log.Config.EndTime.Seconds.ShouldBe(blockTime.Seconds + 100);
        log.Config.ReleasePeriods.Count.ShouldBe(3);
        log.Config.RewardPerSecond.ShouldBe(10);
        log.Config.ReleasePeriods.Count.ShouldBe(3);
        log.Config.ClaimInterval.ShouldBe(10);
        log.Amount.ShouldBe(1000);
        log.PoolId.ShouldBe(HashHelper.ComputeFrom(input));
        log.Config.UpdateAddress = DefaultAddress;

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(log.PoolId);
        output.Status.ShouldBe(true);
        output.PoolInfo.PoolId.ShouldBe(log.PoolId);
        output.PoolInfo.PointsName.ShouldBe(PointsName);
        output.PoolInfo.DappId.ShouldBe(_appId);
        output.PoolInfo.Config.ShouldBe(log.Config);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(new Hash());
        output.PoolInfo.ShouldBeNull();

        var address = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(new Hash());
        address.ShouldBe(new Address());
    }

    [Fact]
    public async Task CreatePointsPoolTests_Fail()
    {
        await Initialize();

        await Register();
        await CreateToken();

        var result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                RewardToken = Symbol,
                RewardPerSecond = 10,
                ReleasePeriods = { },
            });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 10,
                RewardToken = Symbol
            });
        result.TransactionResult.Error.ShouldContain("Invalid points name.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 10,
                RewardToken = Symbol,
                PointsName = "Test"
            });
        result.TransactionResult.Error.ShouldContain("Point not exists.");

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnPointsContractAddress,
            Amount = 1000,
            Symbol = Symbol
        });

        var input = new CreatePointsPoolInput
        {
            DappId = _appId,
            PointsName = PointsName,
            StartTime = BlockTimeProvider.GetBlockTime().Seconds,
            EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
            RewardPerSecond = 10,
            RewardToken = Symbol,
            ReleasePeriods = { 0 },
        };

        await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(input);

        result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Pool exists.");

        input.StartTime = BlockTimeProvider.GetBlockTime().Seconds + 50;
        result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Points name taken.");

        result =
            await UserEcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, 0, "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, 0, "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, 0, "Invalid start time.")]
    [InlineData(Symbol, 1, 0, 0, 0, 0, "Invalid end time.")]
    [InlineData(Symbol, 1, 1, 0, 0, 0, "Invalid reward per second.")]
    [InlineData(Symbol, 1, 1, 1, -1, 0, "Invalid release periods.")]
    [InlineData(Symbol, 1, 1, 1, 0, -1, "Invalid claim interval.")]
    public async Task CreatePointsPoolTests_Config_Fail(string rewardToken, long startTime, long endTime,
        long rewardPerSecond, long releasePeriod, long claimInterval, string error)
    {
        await Initialize();

        await Register();
        await CreateToken();

        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(
            new CreatePointsPoolInput
            {
                DappId = _appId,
                RewardToken = rewardToken,
                StartTime = startTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds : 0,
                EndTime = endTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds + endTime : 0,
                RewardPerSecond = rewardPerSecond,
                ReleasePeriods = { releasePeriod },
                ClaimInterval = claimInterval
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task SetPointsPoolEndTimeTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        var endTime = output.PoolInfo.Config.EndTime;

        var result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = poolId,
                EndTime = endTime.Seconds + 50
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolEndTimeSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(500);
        log.EndTime.ShouldBe(endTime.AddSeconds(50));

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndTime.ShouldBe(endTime.AddSeconds(50));
    }

    [Fact]
    public async Task SetPointsPoolEndTimeTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid end time.");

        var poolInfo = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);

        result = await EcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = poolId,
                EndTime = poolInfo.PoolInfo.Config.StartTime.Seconds
            });
        result.TransactionResult.Error.ShouldContain("Invalid end time.");

        result = await UserEcoEarnPointsContractStub.SetPointsPoolEndTime.SendWithExceptionAsync(
            new SetPointsPoolEndTimeInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task RestartPointsPoolTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        SetBlockTime(100);

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.Status.ShouldBeFalse();

        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var result = await EcoEarnPointsContractStub.RestartPointsPool.SendAsync(new RestartPointsPoolInput
        {
            PoolId = poolId,
            StartTime = blockTime,
            EndTime = blockTime + 100,
            RewardPerSecond = 10,
            RewardToken = Symbol,
            UpdateAddress = DefaultAddress,
            ReleasePeriods = { 0 },
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolRestarted>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(1000);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.Status.ShouldBeTrue();
    }

    [Fact]
    public async Task RestartPointsPoolTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 1,
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Can not restart yet.");

        result =
            await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(new RestartPointsPoolInput());
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                PoolId = poolId,
                UpdateAddress = new Address()
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { },
                RewardPerSecond = 10
            });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 1,
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 1,
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        SetBlockTime(100);

        result = await UserEcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                UpdateAddress = UserAddress,
                RewardToken = Symbol,
                StartTime = BlockTimeProvider.GetBlockTime().Seconds,
                EndTime = BlockTimeProvider.GetBlockTime().Seconds + 100,
                ReleasePeriods = { 0 },
                RewardPerSecond = 1,
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, 0, "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, 0, "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, 0, "Invalid start time.")]
    [InlineData(Symbol, 1, 0, 0, 0, 0, "Invalid end time.")]
    [InlineData(Symbol, 1, 1, 0, 0, 0, "Invalid reward per second.")]
    [InlineData(Symbol, 1, 1, 1, -1, 0, "Invalid release periods.")]
    [InlineData(Symbol, 1, 1, 1, 0, -1, "Invalid claim interval.")]
    public async Task RestartPointsPoolTests_Config_Fail(string rewardToken, long startTime, long endTime,
        long rewardPerSecond, long releasePeriods, long claimInterval, string error)
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();
        
        SetBlockTime(100);

        var result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                PoolId = poolId,
                RewardToken = rewardToken,
                StartTime = startTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds : 0,
                EndTime = endTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds + endTime : 0,
                RewardPerSecond = rewardPerSecond,
                ReleasePeriods = { releasePeriods },
                UpdateAddress = UserAddress,
                ClaimInterval = claimInterval
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task SetPointsPoolRewardPerSecondTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.RewardPerSecond.ShouldBe(10);

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = 100
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolRewardPerSecondSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.RewardPerSecond.ShouldBe(100);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.RewardPerSecond.ShouldBe(100);

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = 100
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(PointsPoolRewardPerSecondSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetPointsPoolRewardPerSecondTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendWithExceptionAsync(
            new SetPointsPoolRewardPerSecondInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendWithExceptionAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendWithExceptionAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendWithExceptionAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid reward per second.");

        result = await UserEcoEarnPointsContractStub.SetPointsPoolRewardPerSecond.SendWithExceptionAsync(
            new SetPointsPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = 0
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetPointsPoolRewardConfigTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriods.Count.ShouldBe(1);
        output.PoolInfo.Config.ReleasePeriods.First().ShouldBe(0);
        output.PoolInfo.Config.ClaimInterval.ShouldBe(100);

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 1, 2 },
                ClaimInterval = 50
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolRewardConfigSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.ReleasePeriods.Data.Count.ShouldBe(2);
        log.ReleasePeriods.Data.First().ShouldBe(1);
        log.ReleasePeriods.Data.Last().ShouldBe(2);
        log.ClaimInterval.ShouldBe(50);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriods.Count.ShouldBe(2);
        output.PoolInfo.Config.ReleasePeriods.First().ShouldBe(1);
        output.PoolInfo.Config.ReleasePeriods.Last().ShouldBe(2);
        output.PoolInfo.Config.ClaimInterval.ShouldBe(50);

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 1, 2 },
                ClaimInterval = 50
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(PointsPoolRewardConfigSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetPointsPoolRewardConfigTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { -1 }
            });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 0 },
                ClaimInterval = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid claim interval.");

        result = await UserEcoEarnPointsContractStub.SetPointsPoolRewardConfig.SendWithExceptionAsync(
            new SetPointsPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 0 }
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }
    
    [Fact]
    public async Task SetDappConfigTests()
    {
        await Initialize();
        await Register();

        var output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.Config.UpdateAddress.ShouldBe(DefaultAddress);

        var result = await EcoEarnPointsContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                UpdateAddress = DefaultAddress
            }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(DappConfigSet)).ShouldBeNull();

        result = await EcoEarnPointsContractStub.SetDappConfig.SendAsync(new SetDappConfigInput
        {
            DappId = _appId,
            Config = new DappConfig
            {
                UpdateAddress = UserAddress
            }
        });
        var log = GetLogEvent<DappConfigSet>(result.TransactionResult);
        log.Config.UpdateAddress.ShouldBe(UserAddress);
        output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.Config.UpdateAddress.ShouldBe(UserAddress);
    }
    
    [Fact]
    public async Task SetDappConfigTests_Fail()
    {
        await Initialize();
        await Register();

        var result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig()
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig
            {
                UpdateAddress = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Config = new DappConfig
            {
                UpdateAddress = UserAddress
            }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        result = await UserEcoEarnPointsContractStub.SetDappConfig.SendWithExceptionAsync(new SetDappConfigInput
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
        await EcoEarnPointsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });
    }

    private async Task<Hash> CreatePointsPool()
    {
        await CreateToken();

        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(new CreatePointsPoolInput
        {
            DappId = _appId,
            PointsName = PointsName,
            StartTime = blockTime,
            EndTime = blockTime + 100,
            RewardPerSecond = 10,
            RewardToken = Symbol,
            ReleasePeriods = { 0 },
            ClaimInterval = 100
        });

        var log = GetLogEvent<PointsPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = log.PoolAddress,
            Amount = 1000,
            Symbol = Symbol
        });

        return log.PoolId;
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
            TotalSupply = 10000,
            Decimals = 0,
            Issuer = DefaultAddress,
            IsBurnable = true,
            IssueChainId = 0,
            LockWhiteList = { TokenContractAddress }
        });
        await TokenContractStub.Issue.SendAsync(new IssueInput
        {
            Amount = 10000,
            Symbol = Symbol,
            To = DefaultAddress
        });
    }
}