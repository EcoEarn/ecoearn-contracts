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
    private const string Symbol = "SGR-1";
    private const string DefaultSymbol = "ELF";
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
            PointsContract = PointsContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress
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
            PointsContract = PointsContractAddress,
            EcoearnRewardsContract = EcoEarnRewardsContractAddress
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
            MinimumAddLiquidityAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnlockWindowDuration = 100,
            ReleasePeriods = { 10, 20, 30 }
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
    [InlineData("", 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, "", "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, "", "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, "", "Invalid start time.")]
    [InlineData(Symbol, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, "", "Invalid end time.")]
    [InlineData(Symbol, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, "", "Invalid reward per second.")]
    [InlineData(Symbol, 1, 1, 100, 0, 0, 0, 1, 0, 0, 0, 0, "", "Invalid fixed boost factor.")]
    [InlineData(Symbol, 1, 1, 100, 1, -1, 0, 1, 0, 0, 0, 0, "", "Invalid minimum amount.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, -1, 1, 0, 0, 0, 0, "", "Invalid minimum add liquidity amount.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 0, 0, 0, 0, 0, "", "Invalid maximum stake duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, -1, 0, 0, 0, "", "Invalid minimum claim amount.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 0, 0, 0, "", "Invalid minimum stake duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 0, 0, "", "Invalid unlock window duration.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 1, -1, DefaultSymbol, "Invalid release periods.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 1, 0, "", "Invalid staking token.")]
    [InlineData(Symbol, 1, 1, 100, 1, 0, 0, 1, 0, 1, 1, 0, "TEST", "TEST not exists.")]
    public async Task CreateTokensPoolTests_Config_Fail(string rewardToken, long startTime, long endTime,
        long rewardPerSecond, long fixedBoostFactor, long minimumAmount, long minimumAddLiquidityAmount, long maximumStakeDuration,
        long minimumClaimAmount, long minimumStakeDuration, long unlockWindowDuration, long releasePeriods, string stakingSymbol,
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
                MaximumStakeDuration = maximumStakeDuration,
                MinimumClaimAmount = minimumClaimAmount,
                MinimumStakeDuration = minimumStakeDuration,
                StakingToken = stakingSymbol,
                UnlockWindowDuration = unlockWindowDuration,
                MinimumAddLiquidityAmount = minimumAddLiquidityAmount,
                ReleasePeriods = { releasePeriods }
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
            MinimumStakeDuration = 1,
            MinimumAddLiquidityAmount = 1
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolStakeConfigSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.MinimumAmount.ShouldBe(1);
        log.MaximumStakeDuration.ShouldBe(1);
        log.MinimumClaimAmount.ShouldBe(1);
        log.MinimumStakeDuration.ShouldBe(1);
        log.MinimumAddLiquidityAmount.ShouldBe(1);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.MinimumAmount.ShouldBe(1);
        output.PoolInfo.Config.MaximumStakeDuration.ShouldBe(1);
        output.PoolInfo.Config.MinimumClaimAmount.ShouldBe(1);
        output.PoolInfo.Config.MinimumStakeDuration.ShouldBe(1);
        output.PoolInfo.Config.MinimumAddLiquidityAmount.ShouldBe(1);

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
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum amount.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid maximum stake duration.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 1,
                MaximumStakeDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum claim amount.");

        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 1,
                MaximumStakeDuration = 1,
                MinimumClaimAmount = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum stake duration.");
        
        result = await EcoEarnTokensContractStub.SetTokensPoolStakeConfig.SendWithExceptionAsync(
            new SetTokensPoolStakeConfigInput
            {
                PoolId = poolId,
                MinimumAmount = 1,
                MaximumStakeDuration = 1,
                MinimumClaimAmount = 1,
                MinimumStakeDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid minimum add liquidity amount.");
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

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        reward.RewardInfos.First().Amount.ShouldBe(99_00000000);

        await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(new SetTokensPoolRewardPerSecondInput
        {
            PoolId = poolId,
            RewardPerSecond = 10_00000000
        });

        SetBlockTime(1);

        var reward2 = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        reward2.RewardInfos.First().Amount.ShouldBe(99_00000000 + 99_0000000);

        await EcoEarnTokensContractStub.SetTokensPoolRewardPerSecond.SendAsync(new SetTokensPoolRewardPerSecondInput
        {
            PoolId = poolId,
            RewardPerSecond = 100_00000000
        });

        SetBlockTime(1);

        var reward3 = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        reward3.RewardInfos.First().Amount.ShouldBe(99_00000000 + 99_0000000 + 99_00000000);
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
            EcoearnRewardsContract = EcoEarnRewardsContractAddress,
            PointsContract = PointsContractAddress
        });
        await EcoEarnRewardsContractStub.Initialize.SendAsync(new Rewards.InitializeInput
        {
            EcoearnTokensContract = EcoEarnTokensContractAddress,
            EcoearnPointsContract = EcoEarnPointsContractAddress
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

    [Fact]
    public async Task SetTokensPoolUnlockWindowDurationTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UnlockWindowDuration.ShouldBe(100);

        var input = new SetTokensPoolUnlockWindowDurationInput
        {
            PoolId = poolId,
            UnlockWindowDuration = 50
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolUnlockWindowDurationSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.UnlockWindowDuration.ShouldBe(50);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UnlockWindowDuration.ShouldBe(50);

        result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolUnlockWindowDurationSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolUnlockWindowDurationTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendWithExceptionAsync(
            new SetTokensPoolUnlockWindowDurationInput());
        result.TransactionResult.Error.ShouldContain("Invalid unlock window duration.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendWithExceptionAsync(
            new SetTokensPoolUnlockWindowDurationInput
            {
                UnlockWindowDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendWithExceptionAsync(
            new SetTokensPoolUnlockWindowDurationInput
            {
                PoolId = new Hash(),
                UnlockWindowDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUnlockWindowDuration.SendWithExceptionAsync(
            new SetTokensPoolUnlockWindowDurationInput
            {
                PoolId = HashHelper.ComputeFrom(1),
                UnlockWindowDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolUnlockWindowDuration.SendWithExceptionAsync(
            new SetTokensPoolUnlockWindowDurationInput
            {
                PoolId = poolId,
                UnlockWindowDuration = 1
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }
    
    [Fact]
    public async Task SetTokensPoolRewardConfigTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriods.Count.ShouldBe(3);

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 1, 2 }
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolRewardConfigSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.ReleasePeriods.Data.Count.ShouldBe(2);
        log.ReleasePeriods.Data.First().ShouldBe(1);
        log.ReleasePeriods.Data.Last().ShouldBe(2);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriods.Count.ShouldBe(2);
        output.PoolInfo.Config.ReleasePeriods.First().ShouldBe(1);
        output.PoolInfo.Config.ReleasePeriods.Last().ShouldBe(2);

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 1, 2 }
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolRewardConfigSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolRewardConfigTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendWithExceptionAsync(
            new SetTokensPoolRewardConfigInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendWithExceptionAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendWithExceptionAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolRewardConfig.SendWithExceptionAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { -1 }
            });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolRewardConfig.SendWithExceptionAsync(
            new SetTokensPoolRewardConfigInput
            {
                PoolId = poolId,
                ReleasePeriods = { 0 }
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }
    
    [Fact]
    public async Task SetTokensPoolMergeIntervalTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.MergeInterval.ShouldBe(0);

        var input = new SetTokensPoolMergeIntervalInput
        {
            PoolId = poolId,
            MergeInterval = 50
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolMergeIntervalSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.MergeInterval.ShouldBe(50);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.MergeInterval.ShouldBe(50);

        result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolMergeIntervalSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolMergeIntervalTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendWithExceptionAsync(
            new SetTokensPoolMergeIntervalInput
            {
                MergeInterval = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid merge interval.");

        result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendWithExceptionAsync(
            new SetTokensPoolMergeIntervalInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendWithExceptionAsync(
            new SetTokensPoolMergeIntervalInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolMergeInterval.SendWithExceptionAsync(
            new SetTokensPoolMergeIntervalInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolMergeInterval.SendWithExceptionAsync(
            new SetTokensPoolMergeIntervalInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
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
            EndTime = blockTime + 100000,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 1,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnlockWindowDuration = 100,
            ReleasePeriods = { 10, 20, 30 },
            MinimumAddLiquidityAmount = 1,
            MergeInterval = 5
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