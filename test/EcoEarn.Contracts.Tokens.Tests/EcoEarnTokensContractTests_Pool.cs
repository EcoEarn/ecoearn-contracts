using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests
{
    private readonly Hash _appId = HashHelper.ComputeFrom("dapp");
    private const string DefaultSymbol = "ELF";
    private const string Symbol = "SGR-1";
    private const string PointsName = "point";

    [Fact]
    public async Task RegisterTests()
    {
        await Initialize();

        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
        await EcoEarnPointsContractStub.Initialize.SendAsync(new Points.InitializeInput
        {
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            PointsContract = PointsContractAddress
        });
        await EcoEarnPointsContractStub.Register.SendAsync(new Points.RegisterInput
        {
            DappId = _appId
        });

        var result = await EcoEarnTokensContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Registered>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.Admin.ShouldBe(DefaultAddress);

        var output = await EcoEarnTokensContractStub.GetDappInfo.CallAsync(_appId);
        output.DappId.ShouldBe(_appId);
        output.Admin.ShouldBe(DefaultAddress);
    }

    [Fact]
    public async Task RegisterTests_Fail()
    {
        var result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput());
        result.TransactionResult.Error.ShouldContain("Not initialized.");

        await Initialize();
        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
        await EcoEarnPointsContractStub.Initialize.SendAsync(new Points.InitializeInput
        {
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            PointsContract = PointsContractAddress
        });
        await EcoEarnPointsContractStub.Register.SendAsync(new Points.RegisterInput
        {
            DappId = _appId
        });

        result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Admin = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = HashHelper.ComputeFrom("test"),
            Admin = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Dapp id not exists.");

        result = await EcoEarnTokensContractUserStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("No permission to register.");

        await EcoEarnTokensContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });

        result = await EcoEarnTokensContractStub.Register.SendWithExceptionAsync(new RegisterInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Dapp registered.");
    }

    [Fact]
    public async Task SetDappAdminTests()
    {
        await Register();

        var output = await EcoEarnTokensContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(DefaultAddress);

        var result = await EcoEarnTokensContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = DefaultAddress
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name == nameof(DappAdminSet)).ShouldBeNull();

        result = await EcoEarnTokensContractStub.SetDappAdmin.SendAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        var log = GetLogEvent<DappAdminSet>(result.TransactionResult);
        log.Admin.ShouldBe(UserAddress);
        output = await EcoEarnTokensContractStub.GetDappInfo.CallAsync(_appId);
        output.Admin.ShouldBe(UserAddress);
    }

    [Fact]
    public async Task SetDappAdminTests_Fail()
    {
        await Register();

        var result = await EcoEarnTokensContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Dapp not exists.");

        result = await EcoEarnTokensContractStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid admin.");

        result = await EcoEarnTokensContractUserStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
        {
            DappId = _appId,
            Admin = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task CreateTokensPoolTests()
    {
        await Register();
        await CreateToken();

        var poolCount = await EcoEarnTokensContractStub.GetPoolCount.CallAsync(_appId);
        poolCount.Value.ShouldBe(0);

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 1000000_00000000,
            Symbol = Symbol
        });

        var blockTime = BlockTimeProvider.GetBlockTime();

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime.Seconds,
            EndTime = blockTime.Seconds + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 1000,
            MaximumStakeDuration = 1000000000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnlockWindowDuration = 100
        };

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.Amount.ShouldBe(1000_00000000);
        log.PoolId.ShouldBe(HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(poolCount.Value),
            HashHelper.ComputeFrom(input)));

        poolCount = await EcoEarnTokensContractStub.GetPoolCount.CallAsync(_appId);
        poolCount.Value.ShouldBe(1);

        var poolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(log.PoolId);
        poolInfo.Status.ShouldBeTrue();
        poolInfo.PoolInfo.PoolId.ShouldBe(log.PoolId);
        poolInfo.PoolInfo.DappId.ShouldBe(_appId);
        poolInfo.PoolInfo.Config.ShouldBe(log.Config);

        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(log.PoolId);
        poolData.PoolId.ShouldBe(log.PoolId);
        poolData.TotalStakedAmount.ShouldBe(0);
        poolData.AccTokenPerShare.ShouldBeNull();
        poolData.LastRewardTime.Seconds.ShouldBe(blockTime.Seconds);
    }

    [Fact]
    public async Task CreateTokensPoolTests_Fail()
    {
        await Register();
        await CreateToken();

        var result =
            await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput());
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnTokensContractUserStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            RewardTokenContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid reward token contract.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            StakeTokenContract = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid stake token contract.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid start time.")]
    [InlineData(Symbol, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid end time.")]
    [InlineData(Symbol, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid reward per second.")]
    [InlineData(Symbol, 1, 1, 100, 0, 0, 0, 0, 0, 0, 0, "", "Invalid fixed boost factor.")]
    [InlineData(Symbol, 1, 1, 100, 1, -1, 0, 0, 0, 0, 0, "", "Invalid minimum amount.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, -1, 0, 0, 0, 0, "", "Invalid release period.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 0, 0, 0, 0, "", "Invalid maximum stake duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, -1, 0, 0, "", "Invalid minimum claim amount.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 0, 0, "", "Invalid minimum stake duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 0, "", "Invalid unlock window duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 1, "", "Invalid staking token.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 1, "TEST", "TEST not exists.")]
    public async Task CreateTokensPoolTests_Config_Fail(string rewardToken, long startTime, long endTime,
        long rewardPerSecond, long fixedBoostFactor, long minimumAmount, long releasePeriod, long maximumStakeDuration,
        long minimumClaimAmount, long minimumStakeDuration, long unlockWindowDuration, string stakingSymbol,
        string error)
    {
        await Register();
        await CreateToken();

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(
            new CreateTokensPoolInput
            {
                DappId = _appId,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                RewardToken = rewardToken,
                StartTime = startTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds : 0,
                EndTime = endTime == 1 ? BlockTimeProvider.GetBlockTime().Seconds + endTime : 0,
                RewardPerSecond = rewardPerSecond,
                FixedBoostFactor = fixedBoostFactor,
                MinimumAmount = minimumAmount,
                ReleasePeriod = releasePeriod,
                MaximumStakeDuration = maximumStakeDuration,
                MinimumClaimAmount = minimumClaimAmount,
                MinimumStakeDuration = minimumStakeDuration,
                StakingToken = stakingSymbol,
                UnlockWindowDuration = unlockWindowDuration
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task SetTokensPoolEndTimeTests()
    {
        const long rewardBalance = 500_00000000;

        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        var endTime = output.PoolInfo.Config.EndTime;

        var input = new SetTokensPoolEndTimeInput
        {
            PoolId = poolId,
            EndTime = endTime.Seconds + 5
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolEndTimeSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(rewardBalance);
        log.EndTime.ShouldBe(output.PoolInfo.Config.EndTime.AddSeconds(5));

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndTime.ShouldBe(endTime.AddSeconds(5));
    }

    [Fact]
    public async Task SetTokensPoolEndTimeTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid end time.");

        var poolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);

        result = await EcoEarnTokensContractStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput
            {
                PoolId = poolId,
                EndTime = poolInfo.PoolInfo.Config.StartTime.Seconds
            });
        result.TransactionResult.Error.ShouldContain("Invalid end time.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolEndTime.SendWithExceptionAsync(
            new SetTokensPoolEndTimeInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetTokensPoolRewardReleasePeriodTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriod.ShouldBe(10);

        var input = new SetTokensPoolRewardReleasePeriodInput
        {
            PoolId = poolId,
            ReleasePeriod = 100
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolRewardReleasePeriodSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.ReleasePeriod.ShouldBe(100);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriod.ShouldBe(100);

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolRewardReleasePeriodSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolRewardReleasePeriodTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetTokensPoolRewardReleasePeriodInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetTokensPoolRewardReleasePeriodInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetTokensPoolRewardReleasePeriodInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetTokensPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid release period.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetTokensPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = 0
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetTokensPoolStakeConfigTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.FixedBoostFactor.ShouldBe(1);

        var input = new SetTokensPoolStakeConfigInput
        {
            PoolId = poolId,
            MinimumAmount = 1,
            MaximumStakeDuration = 1,
            MinimumClaimAmount = 1,
            MinimumStakeDuration = 1
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolStakeConfigSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.MinimumAmount.ShouldBe(1);
        log.MaximumStakeDuration.ShouldBe(1);
        log.MinimumClaimAmount.ShouldBe(1);
        log.MinimumStakeDuration.ShouldBe(1);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.MinimumAmount.ShouldBe(1);
        output.PoolInfo.Config.MaximumStakeDuration.ShouldBe(1);
        output.PoolInfo.Config.MinimumClaimAmount.ShouldBe(1);
        output.PoolInfo.Config.MinimumStakeDuration.ShouldBe(1);

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolStakeConfigSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolStakeConfigTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum amount.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 0,
                MaximumStakeDuration = 0
            });
        result.TransactionResult.Error.ShouldContain("Invalid maximum stake duration.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 0,
                MaximumStakeDuration = 1,
                MinimumClaimAmount = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum claim amount.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 0,
                MaximumStakeDuration = 1,
                MinimumClaimAmount = 0,
                MinimumStakeDuration = 0
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum stake duration.");
    }

    [Fact]
    public async Task SetTokensPoolFixedBoostFactorTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.FixedBoostFactor.ShouldBe(1);

        var input = new SetTokensPoolFixedBoostFactorInput
        {
            PoolId = poolId,
            FixedBoostFactor = 100
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolFixedBoostFactorSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.FixedBoostFactor.ShouldBe(100);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.FixedBoostFactor.ShouldBe(100);

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolFixedBoostFactorSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolFixedBoostFactorTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput());
        result.TransactionResult.Error.ShouldContain("Invalid fixed boost factor.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                FixedBoostFactor = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = new Hash(),
                FixedBoostFactor = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = HashHelper.ComputeFrom(1),
                FixedBoostFactor = 1
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = poolId,
                FixedBoostFactor = 1
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetTokensPoolRewardPerSecondTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.RewardPerSecond.ShouldBe(100_00000000);

        var input = new SetTokensPoolRewardPerSecondInput
        {
            PoolId = poolId,
            RewardPerSecond = 10_00000000
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolRewardPerSecondSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.RewardPerSecond.ShouldBe(10_00000000);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.RewardPerSecond.ShouldBe(10_00000000);

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolRewardPerSecondSet)))
            .ShouldBeNull();

        const long tokenBalance = 5_00000000;

        poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);

        SetBlockTime(1);

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        reward.Amount.ShouldBe(99_00000000);

        await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(new SetTokensPoolRewardPerSecondInput
        {
            PoolId = poolId,
            RewardPerSecond = 10_00000000
        });

        SetBlockTime(1);

        var reward2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        reward2.Amount.ShouldBe(99_00000000 + 99_0000000);

        await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(new SetTokensPoolRewardPerSecondInput
        {
            PoolId = poolId,
            RewardPerSecond = 100_00000000
        });

        SetBlockTime(1);

        var reward3 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        reward3.Amount.ShouldBe(99_00000000 + 99_0000000 + 99_00000000);
    }

    [Fact]
    public async Task SetTokensPoolRewardPerSecondTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendWithExceptionAsync(
            new SetTokensPoolRewardPerSecondInput
            {
                RewardPerSecond = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendWithExceptionAsync(
            new SetTokensPoolRewardPerSecondInput
            {
                RewardPerSecond = 1,
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendWithExceptionAsync(
            new SetTokensPoolRewardPerSecondInput
            {
                RewardPerSecond = 1,
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendWithExceptionAsync(
            new SetTokensPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = 0
            });
        result.TransactionResult.Error.ShouldContain("Invalid reward per second.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolRewardPerSecond.SendWithExceptionAsync(
            new SetTokensPoolRewardPerSecondInput
            {
                PoolId = poolId,
                RewardPerSecond = 1
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task Register()
    {
        await Initialize();
        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
        await EcoEarnPointsContractStub.Initialize.SendAsync(new Points.InitializeInput
        {
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            PointsContract = PointsContractAddress
        });
        await EcoEarnPointsContractStub.Register.SendAsync(new Points.RegisterInput
        {
            DappId = _appId
        });
        await EcoEarnTokensContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
    }

    private async Task<Hash> CreateTokensPool()
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await Register();
            await CreateToken();
        }

        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 100,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 1,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 100000_00000000,
            To = log.AddressInfo.RewardAddress,
            Symbol = Symbol
        });

        return log.PoolId;
    }

    private async Task<Hash> CreateTokensPool(string symbol)
    {
        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = symbol,
            FixedBoostFactor = 10000,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);

        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 1000_00000000,
            To = log.AddressInfo.RewardAddress,
            Symbol = Symbol
        });

        return log.PoolId;
    }

    private async Task<Hash> CreateTokensPoolWithLongEndTime()
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await Register();
            await CreateToken();
        }

        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 86401,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 1,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 10000000_00000000,
            To = log.AddressInfo.RewardAddress,
            Symbol = Symbol
        });

        return log.PoolId;
    }

    private async Task<Hash> CreateTokensPoolWithLowRewardPerSecond()
    {
        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 10000,
            MaximumStakeDuration = 500000,
            MinimumAmount = 2_00000000,
            MinimumClaimAmount = 1,
            RewardPerSecond = 1_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);

        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 1000_00000000,
            To = log.AddressInfo.RewardAddress,
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
        var seedExpTime = "1720590467";
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
                        seedExpTime
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