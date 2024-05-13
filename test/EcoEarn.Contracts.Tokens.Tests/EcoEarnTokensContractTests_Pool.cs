using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Types;
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

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 10,
                RewardToken = Symbol,
                StakingToken = Symbol,
                FixedBoostFactor = 1000,
                MaximumStakeDuration = 1000000000,
                MinimumAmount = 1_00000000,
                MinimumClaimAmount = 1_00000000,
                RewardPerBlock = 100_00000000,
                ReleasePeriod = 10,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                UpdateAddress = DefaultAddress,
                MinimumStakeDuration = 1
            }
        };

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolCreated>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.Amount.ShouldBe(1000_00000000);
        log.Config.ShouldBe(input.Config);
        log.PoolId.ShouldBe(HashHelper.ConcatAndCompute(HashHelper.ComputeFrom(poolCount.Value),
            HashHelper.ComputeFrom(input)));

        poolCount = await EcoEarnTokensContractStub.GetPoolCount.CallAsync(_appId);
        poolCount.Value.ShouldBe(1);

        var poolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(log.PoolId);
        poolInfo.Status.ShouldBeTrue();
        poolInfo.PoolInfo.PoolId.ShouldBe(log.PoolId);
        poolInfo.PoolInfo.DappId.ShouldBe(_appId);
        poolInfo.PoolInfo.Config.ShouldBe(input.Config);

        var poolData = await EcoEarnTokensContractStub.GetPoolData.CallAsync(log.PoolId);
        poolData.PoolId.ShouldBe(log.PoolId);
        poolData.TotalStakedAmount.ShouldBe(0);
        poolData.AccTokenPerShare.ShouldBeNull();
        poolData.LastRewardBlock.ShouldBe(blockNumber);

        var poolAddress = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(log.PoolId);
        GetTokenBalance(Symbol, poolAddress.RewardAddress).Result.ShouldBe(1000_00000000);
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
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("Invalid config.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig()
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                UpdateAddress = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                UpdateAddress = DefaultAddress,
                RewardTokenContract = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid reward token contract.");

        result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                UpdateAddress = DefaultAddress,
                StakeTokenContract = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid stake token contract.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid start block number.")]
    [InlineData(Symbol, 100, 0, 0, 0, 0, 0, 0, 0, 0, "", "Invalid end block number.")]
    [InlineData(Symbol, 100, 150, 0, 0, 0, 0, 0, 0, 0, "", "Invalid reward per block.")]
    [InlineData(Symbol, 100, 150, 100, -1, 0, 0, 0, 0, 0, "", "Invalid fixed boost factor.")]
    [InlineData(Symbol, 100, 150, 100, 0, -1, 0, 0, 0, 0, "", "Invalid minimum amount.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, -1, 0, 0, 0, "", "Invalid release period.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, 0, 0, 0, 0, "", "Invalid maximum stake duration.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, 0, 1, -1, 0, "", "Invalid minimum claim amount.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, 0, 1, 0, 0, "", "Invalid minimum stake duration.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, 0, 1, 0, 1, "", "Invalid staking token.")]
    [InlineData(Symbol, 100, 150, 100, 0, 0, 0, 1, 0, 1, "TEST", "TEST not exists.")]
    public async Task CreateTokensPoolTests_Config_Fail(string rewardToken, long startBlockNumber, long endBlockNumber,
        long rewardPerBlock, long fixedBoostFactor, long minimumAmount, long releasePeriod, long maximumStakeDuration,
        long minimumClaimAmount, long minimumStakeDuration, string stakingSymbol, string error)
    {
        await Register();
        await CreateToken();

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendWithExceptionAsync(
            new CreateTokensPoolInput
            {
                DappId = _appId,
                Config = new TokensPoolConfig
                {
                    UpdateAddress = DefaultAddress,
                    RewardTokenContract = TokenContractAddress,
                    StakeTokenContract = TokenContractAddress,
                    RewardToken = rewardToken,
                    StartBlockNumber = startBlockNumber,
                    EndBlockNumber = endBlockNumber,
                    RewardPerBlock = rewardPerBlock,
                    FixedBoostFactor = fixedBoostFactor,
                    MinimumAmount = minimumAmount,
                    ReleasePeriod = releasePeriod,
                    MaximumStakeDuration = maximumStakeDuration,
                    MinimumClaimAmount = minimumClaimAmount,
                    MinimumStakeDuration = minimumStakeDuration,
                    StakingToken = stakingSymbol,
                }
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task CloseTokensPoolTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.Status.ShouldBeTrue();

        var result = await EcoEarnTokensContractStub.CloseTokensPool.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolClosed>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Config.EndBlockNumber.ShouldBeLessThan(output.PoolInfo.Config.EndBlockNumber);
        log.Config.EndBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.Status.ShouldBeFalse();
        output.PoolInfo.Config.EndBlockNumber.ShouldBe(log.Config.EndBlockNumber);
    }

    [Fact]
    public async Task CloseTokensPoolTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.CloseTokensPool.SendWithExceptionAsync(new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.CloseTokensPool.SendWithExceptionAsync(HashHelper.ComputeFrom("test"));
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.CloseTokensPool.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("No permission.");

        await EcoEarnTokensContractStub.CloseTokensPool.SendAsync(poolId);

        result = await EcoEarnTokensContractStub.CloseTokensPool.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Pool already closed.");
    }

    [Fact]
    public async Task SetTokensPoolEndBlockNumberTests()
    {
        const long rewardBalance = 1000_00000000;

        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        var endBlockNumber = output.PoolInfo.Config.EndBlockNumber;

        var address = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(rewardBalance);

        var result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = endBlockNumber - 5
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        var log = GetLogEvent<TokensPoolEndBlockNumberSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(0);
        log.EndBlockNumber.ShouldBe(endBlockNumber - 5);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBe(endBlockNumber - 5);

        balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(rewardBalance);

        var input = new SetTokensPoolEndBlockNumberInput
        {
            PoolId = poolId,
            EndBlockNumber = endBlockNumber + 5
        };

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        log = GetLogEvent<TokensPoolEndBlockNumberSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(rewardBalance);
        log.EndBlockNumber.ShouldBe(input.EndBlockNumber);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBe(input.EndBlockNumber);

        balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(rewardBalance * 2);

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolEndBlockNumberSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolEndBlockNumberTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid end block number.");

        var poolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);

        result = await EcoEarnTokensContractStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = poolInfo.PoolInfo.Config.StartBlockNumber
            });
        result.TransactionResult.Error.ShouldContain("Invalid end block number.");

        await EcoEarnTokensContractStub.CloseTokensPool.SendAsync(poolId);

        result = await EcoEarnTokensContractUserStub.SetTokensPoolEndBlockNumber.SendWithExceptionAsync(
            new SetTokensPoolEndBlockNumberInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetTokensPoolUpdateAddressTests()
    {
        var poolId = await CreateTokensPool();

        var output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UpdateAddress.ShouldBe(DefaultAddress);

        var input = new SetTokensPoolUpdateAddressInput
        {
            PoolId = poolId,
            UpdateAddress = UserAddress
        };

        var result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokensPoolUpdateAddressSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.UpdateAddress.ShouldBe(UserAddress);

        output = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UpdateAddress.ShouldBe(UserAddress);

        result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendAsync(input);
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(TokensPoolUpdateAddressSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetTokensPoolUpdateAddressTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput());
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput
            {
                UpdateAddress = new Address()
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress,
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress,
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolUpdateAddress.SendWithExceptionAsync(
            new SetTokensPoolUpdateAddressInput
            {
                PoolId = poolId,
                UpdateAddress = DefaultAddress
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
        output.PoolInfo.Config.FixedBoostFactor.ShouldBe(10000);

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
        output.PoolInfo.Config.FixedBoostFactor.ShouldBe(10000);

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
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = poolId,
                FixedBoostFactor = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid fixed boost factor.");

        result = await EcoEarnTokensContractUserStub.SetTokensPoolFixedBoostFactor.SendWithExceptionAsync(
            new SetTokensPoolFixedBoostFactorInput
            {
                PoolId = poolId,
                FixedBoostFactor = 0
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
        await Register();
        await CreateToken();

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 1000000_00000000,
            Symbol = Symbol
        });

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 10,
                RewardToken = Symbol,
                StakingToken = Symbol,
                FixedBoostFactor = 10000,
                MaximumStakeDuration = 500000,
                MinimumAmount = 1_00000000,
                MinimumClaimAmount = 1_00000000,
                RewardPerBlock = 100_00000000,
                ReleasePeriod = 10,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                UpdateAddress = DefaultAddress,
                MinimumStakeDuration = 86400
            }
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }
}