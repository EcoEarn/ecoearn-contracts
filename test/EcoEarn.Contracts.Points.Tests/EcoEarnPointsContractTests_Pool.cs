using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Types;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests
{
    private const string PointsName = "point";
    private const string DefaultSymbol = "ELF";
    private const string Symbol = "SGR-1";
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

        var output = await EcoEarnPointsContractStub.GetDappInfo.CallAsync(_appId);
        output.DappId.ShouldBe(_appId);
        output.Admin.ShouldBe(DefaultAddress);
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
            var result = await EcoEarnPointsContractUserStub.Register.SendWithExceptionAsync(new RegisterInput
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

        result = await EcoEarnPointsContractUserStub.SetDappAdmin.SendWithExceptionAsync(new SetDappAdminInput
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

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var input = new CreatePointsPoolInput
        {
            DappId = _appId,
            PointsName = PointsName,
            Config = new PointsPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 100,
                RewardPerBlock = 10,
                RewardToken = Symbol,
                UpdateAddress = DefaultAddress,
                ReleasePeriod = 0
            }
        };
        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolCreated>(result.TransactionResult);
        log.DappId.ShouldBe(_appId);
        log.PointsName.ShouldBe(PointsName);
        log.Config.RewardToken.ShouldBe(Symbol);
        log.Config.StartBlockNumber.ShouldBe(blockNumber);
        log.Config.EndBlockNumber.ShouldBe(blockNumber + 100);
        log.Config.ReleasePeriod.ShouldBe(0);
        log.Config.UpdateAddress.ShouldBe(DefaultAddress);
        log.Config.RewardPerBlock.ShouldBe(10);
        log.Amount.ShouldBe(1000);
        log.PoolId.ShouldBe(HashHelper.ComputeFrom(input));

        {
            var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(log.PoolId);
            output.Status.ShouldBe(true);
            output.PoolInfo.PoolId.ShouldBe(log.PoolId);
            output.PoolInfo.PointsName.ShouldBe(PointsName);
            output.PoolInfo.DappId.ShouldBe(_appId);
            output.PoolInfo.Config.ShouldBe(input.Config);
        }
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
                DappId = _appId
            });
        result.TransactionResult.Error.ShouldContain("Invalid config.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                Config = new PointsPoolConfig()
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                Config = new PointsPoolConfig
                {
                    UpdateAddress = new Address()
                }
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                Config = new PointsPoolConfig
                {
                    UpdateAddress = DefaultAddress,
                    StartBlockNumber = 100,
                    EndBlockNumber = 200,
                    ReleasePeriod = 2,
                    RewardPerBlock = 10,
                    RewardToken = Symbol
                }
            });
        result.TransactionResult.Error.ShouldContain("Invalid points name.");

        result =
            await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId,
                Config = new PointsPoolConfig
                {
                    UpdateAddress = DefaultAddress,
                    StartBlockNumber = 100,
                    EndBlockNumber = 200,
                    ReleasePeriod = 2,
                    RewardPerBlock = 10,
                    RewardToken = Symbol
                },
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
            Config = new PointsPoolConfig
            {
                StartBlockNumber = 100,
                EndBlockNumber = 200,
                RewardPerBlock = 10,
                RewardToken = Symbol,
                UpdateAddress = DefaultAddress,
                ReleasePeriod = 0
            }
        };

        await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(input);

        result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Pool exists.");

        input.Config.StartBlockNumber = 150;
        result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(input);
        result.TransactionResult.Error.ShouldContain("Points name taken.");

        result =
            await EcoEarnPointsContractUserStub.CreatePointsPool.SendWithExceptionAsync(new CreatePointsPoolInput
            {
                DappId = _appId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, "Invalid start block number.")]
    [InlineData(Symbol, 100, 100, 0, 0, "Invalid end block number.")]
    [InlineData(Symbol, 100, 200, 0, 0, "Invalid reward per block.")]
    [InlineData(Symbol, 100, 200, 10, -1, "Invalid release period.")]
    public async Task CreatePointsPoolTests_Config_Fail(string rewardToken, long startBlockNumber, long endBlockNumber,
        long rewardPerBlock, long releasePeriod, string error)
    {
        await Initialize();

        await Register();
        await CreateToken();

        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendWithExceptionAsync(
            new CreatePointsPoolInput
            {
                DappId = _appId,
                Config = new PointsPoolConfig
                {
                    RewardToken = rewardToken,
                    StartBlockNumber = startBlockNumber,
                    EndBlockNumber = endBlockNumber,
                    RewardPerBlock = rewardPerBlock,
                    ReleasePeriod = releasePeriod,
                    UpdateAddress = UserAddress
                }
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task ClosePointsPoolTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBeGreaterThan(blockNumber);
        output.Status.ShouldBeTrue();

        var result = await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolClosed>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Config.EndBlockNumber.ShouldBeLessThan(output.PoolInfo.Config.EndBlockNumber);

        blockNumber = SimulateBlockMining().Result.Block.Height;

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBeLessThan(blockNumber);
        output.Status.ShouldBeFalse();
    }

    [Fact]
    public async Task ClosePointsPoolTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.ClosePointsPool.SendWithExceptionAsync(
            new Hash());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.ClosePointsPool.SendWithExceptionAsync(HashHelper.ComputeFrom(1));
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        result = await EcoEarnPointsContractStub.ClosePointsPool.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("Pool already closed.");

        result = await EcoEarnPointsContractUserStub.ClosePointsPool.SendWithExceptionAsync(poolId);
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetPointsPoolEndBlockNumberTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        var endBlockNumber = output.PoolInfo.Config.EndBlockNumber;

        var address = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, address);
        balance.ShouldBe(1000);

        var result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = endBlockNumber - 50
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
        var log = GetLogEvent<PointsPoolEndBlockNumberSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(0);
        log.EndBlockNumber.ShouldBe(endBlockNumber - 50);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBe(endBlockNumber - 50);

        balance = await GetTokenBalance(Symbol, address);
        balance.ShouldBe(1000);

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnPointsContractAddress,
            Symbol = Symbol,
            Amount = 1000
        });

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = endBlockNumber + 50
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        log = GetLogEvent<PointsPoolEndBlockNumberSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Amount.ShouldBe(1000);
        log.EndBlockNumber.ShouldBe(endBlockNumber + 50);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.EndBlockNumber.ShouldBe(endBlockNumber + 50);

        balance = await GetTokenBalance(Symbol, address);
        balance.ShouldBe(2000);

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = endBlockNumber + 50
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(PointsPoolEndBlockNumberSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetPointsPoolEndBlockNumberTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid end block number.");

        var poolInfo = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);

        result = await EcoEarnPointsContractStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput
            {
                PoolId = poolId,
                EndBlockNumber = poolInfo.PoolInfo.Config.StartBlockNumber
            });
        result.TransactionResult.Error.ShouldContain("Invalid end block number.");

        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        result = await EcoEarnPointsContractUserStub.SetPointsPoolEndBlockNumber.SendWithExceptionAsync(
            new SetPointsPoolEndBlockNumberInput
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
        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.Status.ShouldBeFalse();

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnPointsContractAddress,
            Amount = 1000,
            Symbol = Symbol
        });

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var config = new PointsPoolConfig
        {
            StartBlockNumber = blockNumber,
            EndBlockNumber = blockNumber + 100,
            RewardPerBlock = 10,
            RewardToken = Symbol,
            UpdateAddress = DefaultAddress,
            ReleasePeriod = 10
        };

        var result = await EcoEarnPointsContractStub.RestartPointsPool.SendAsync(new RestartPointsPoolInput
        {
            PoolId = poolId,
            Config = config
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolRestarted>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.Config.ShouldBe(config);
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

        var result =
            await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(new RestartPointsPoolInput());
        result.TransactionResult.Error.ShouldContain("Invalid config.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                PoolId = poolId,
                Config = new PointsPoolConfig()
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                PoolId = poolId,
                Config = new PointsPoolConfig
                {
                    UpdateAddress = new Address()
                }
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        var config = new PointsPoolConfig
        {
            UpdateAddress = UserAddress,
            RewardToken = Symbol,
            StartBlockNumber = 100,
            EndBlockNumber = 200,
            ReleasePeriod = 10,
            RewardPerBlock = 1
        };
        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                Config = config
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                Config = config,
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                Config = config,
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                Config = config,
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Can not restart yet.");

        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        result = await EcoEarnPointsContractUserStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                Config = config,
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Theory]
    [InlineData("", 0, 0, 0, 0, "Invalid reward token.")]
    [InlineData("TEST", 0, 0, 0, 0, "TEST not exists.")]
    [InlineData(Symbol, 0, 0, 0, 0, "Invalid start block number.")]
    [InlineData(Symbol, 100, 0, 0, 0, "Invalid end block number.")]
    [InlineData(Symbol, 100, 200, 0, 0, "Invalid reward per block.")]
    [InlineData(Symbol, 100, 200, 10, -1, "Invalid release period.")]
    public async Task RestartPointsPoolTests_Config_Fail(string rewardToken, long startBlockNumber, long endBlockNumber,
        long rewardPerBlock, long releasePeriod, string error)
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        var result = await EcoEarnPointsContractStub.RestartPointsPool.SendWithExceptionAsync(
            new RestartPointsPoolInput
            {
                PoolId = poolId,
                Config = new PointsPoolConfig
                {
                    RewardToken = rewardToken,
                    StartBlockNumber = startBlockNumber,
                    EndBlockNumber = endBlockNumber,
                    RewardPerBlock = rewardPerBlock,
                    ReleasePeriod = releasePeriod,
                    UpdateAddress = UserAddress
                }
            });
        result.TransactionResult.Error.ShouldContain(error);
    }

    [Fact]
    public async Task SetPointsPoolUpdateAddressTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UpdateAddress.ShouldBe(DefaultAddress);

        var result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendAsync(
            new SetPointsPoolUpdateAddressInput
            {
                PoolId = poolId,
                UpdateAddress = UserAddress
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolUpdateAddressSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.UpdateAddress.ShouldBe(UserAddress);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.UpdateAddress.ShouldBe(UserAddress);

        result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendAsync(
            new SetPointsPoolUpdateAddressInput
            {
                PoolId = poolId,
                UpdateAddress = UserAddress
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(PointsPoolUpdateAddressSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetPointsPoolUpdateAddressTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput());
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput
            {
                UpdateAddress = new Address()
            });
        result.TransactionResult.Error.ShouldContain("Invalid update address.");

        result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress,
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput
            {
                UpdateAddress = UserAddress,
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractUserStub.SetPointsPoolUpdateAddress.SendWithExceptionAsync(
            new SetPointsPoolUpdateAddressInput
            {
                PoolId = poolId,
                UpdateAddress = DefaultAddress
            });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task SetPointsPoolRewardReleasePeriodTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriod.ShouldBe(10);

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = 100
            });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<PointsPoolRewardReleasePeriodSet>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.ReleasePeriod.ShouldBe(100);

        output = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(poolId);
        output.PoolInfo.Config.ReleasePeriod.ShouldBe(100);

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = 100
            });
        result.TransactionResult.Logs.FirstOrDefault(l => l.Name.Contains(nameof(PointsPoolRewardReleasePeriodSet)))
            .ShouldBeNull();
    }

    [Fact]
    public async Task SetPointsPoolRewardReleasePeriodTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetPointsPoolRewardReleasePeriodInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.SetPointsPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = -1
            });
        result.TransactionResult.Error.ShouldContain("Invalid release period.");

        result = await EcoEarnPointsContractUserStub.SetPointsPoolRewardReleasePeriod.SendWithExceptionAsync(
            new SetPointsPoolRewardReleasePeriodInput
            {
                PoolId = poolId,
                ReleasePeriod = 0
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

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnPointsContractAddress,
            Amount = 10000,
            Symbol = Symbol
        });

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var result = await EcoEarnPointsContractStub.CreatePointsPool.SendAsync(new CreatePointsPoolInput
        {
            DappId = _appId,
            PointsName = PointsName,
            Config = new PointsPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 100,
                RewardPerBlock = 10,
                RewardToken = Symbol,
                UpdateAddress = DefaultAddress,
                ReleasePeriod = 10
            }
        });

        return GetLogEvent<PointsPoolCreated>(result.TransactionResult).PoolId;
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