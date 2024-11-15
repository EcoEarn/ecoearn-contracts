using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests
{
    [Fact]
    public async Task ClaimTests()
    {
        const long tokenBalance = 5_00000000;

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);

        SetBlockTime(10);

        var reward = 100_00000000 * 10 - 100_00000000 * 10 / 100;

        var rewards = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        rewards.RewardInfos.First().Amount.ShouldBe(reward);

        var result = await UserEcoEarnTokensContractStub.Claim.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult, 1);
        log.ClaimInfos.Data.Count.ShouldBe(3);

        var claimInfo = log.ClaimInfos.Data[0];
        claimInfo.ClaimId.ShouldNotBeNull();
        claimInfo.PoolId.ShouldBe(poolId);
        claimInfo.ClaimedAmount.ShouldBe(reward / 3);
        claimInfo.ClaimedSymbol.ShouldBe(Symbol);
        claimInfo.ClaimedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        claimInfo.ClaimedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);
        claimInfo.ReleaseTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(10));
        claimInfo.Seed.ShouldBeNull();
        claimInfo.ContractAddress.ShouldBe(EcoEarnTokensContractAddress);

        EcoEarnRewardsContractStub.GetClaimInfo.CallAsync(claimInfo.ClaimId).Result.ShouldBe(claimInfo);

        claimInfo = log.ClaimInfos.Data[1];
        claimInfo.ClaimedAmount.ShouldBe(reward / 3);
        claimInfo.ReleaseTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(20));

        claimInfo = log.ClaimInfos.Data[2];
        claimInfo.ClaimedAmount.ShouldBe(reward / 3);
        claimInfo.ReleaseTime.ShouldBe(BlockTimeProvider.GetBlockTime().AddSeconds(30));
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        var result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput());
        result.TransactionResult.Error.ShouldContain("Not initialized.");

        await EcoEarnRewardsContractStub.Initialize.SendAsync(new InitializeInput
        {
            EcoearnPointsContract = DefaultAddress,
            EcoearnTokensContract = DefaultAddress,
            PointsContract = PointsContractAddress,
            UpdateAddress = DefaultAddress
        });

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid symbol.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol,
            Amount = 1,
            ReleasePeriods = { }
        });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol,
            Amount = 1,
            ReleasePeriods = { -1 }
        });
        result.TransactionResult.Error.ShouldContain("Invalid release periods.");

        result = await EcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol,
            Amount = 1,
            ReleasePeriods = { 0 },
            Seed = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnRewardsContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Symbol = Symbol,
            Amount = 1,
            ReleasePeriods = { 0 },
            Seed = HashHelper.ComputeFrom("seed")
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task WithdrawTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        SetBlockTime(10);

        var claimIds = Claim(poolId).Result.Select(c => c.ClaimId).ToList();

        var input = new WithdrawInput
        {
            ClaimIds = { claimIds },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, claimIds, 1_00000000, UserAddress, seed,
                expirationTime, _appId),
            DappId = _appId
        };

        var result = await UserEcoEarnRewardsContractStub.Withdraw.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Withdrawn>(result.TransactionResult);
        log.ClaimIds.Data.Count.ShouldBe(3);

        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(1_00000000);
    }

    [Fact]
    public async Task WithdrawTests_Fail()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        SetBlockTime(10);
        var claimIds = Claim(poolId).Result.Select(c => c.ClaimId).ToList();

        var result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput());
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { }
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Dapp id not exists.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("Invalid expiration time.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = _appId,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddSeconds(-1).Seconds
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = _appId,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddSeconds(-1).Seconds,
            Signature = ByteString.Empty
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = _appId,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddSeconds(-1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Signature expired.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            DappId = _appId,
            ExpirationTime = expirationTime,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, new List<Hash> { new Hash() }, 1, DefaultAddress,
                seed, expirationTime, _appId),
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { HashHelper.ComputeFrom("test") },
            Account = DefaultAddress,
            Amount = 1,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, new List<Hash> { HashHelper.ComputeFrom("test") },
                1, DefaultAddress, seed, expirationTime, _appId),
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("Claim id not exists.");

        result = await UserEcoEarnRewardsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimIds },
            Account = DefaultAddress,
            Amount = 10000_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, claimIds, 10000_00000000, DefaultAddress,
                seed, expirationTime, _appId),
            DappId = _appId
        });
        result.TransactionResult.Error.ShouldContain("Amount too much.");
    }

    [Fact]
    public async Task StakeRewardsTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);

        SetBlockTime(10);

        var claimIds = Claim(poolId).Result.Select(c => c.ClaimId).ToList();

        var stakeInput = new StakeInput
        {
            ClaimIds = { claimIds },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = _appId,
            PoolId = poolId,
            Period = 100,
            LongestReleaseTime = BlockTimeProvider.GetBlockTime().Seconds
        };

        var input = new StakeRewardsInput
        {
            StakeInput = stakeInput,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, new StakeRewardsInput
            {
                StakeInput = stakeInput
            })
        };

        SetBlockTime(100);

        var result = await UserEcoEarnRewardsContractStub.StakeRewards.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<RewardsStaked>(result.TransactionResult);
        log.ClaimIds.Data.Count.ShouldBe(3);
        log.Account.ShouldBe(UserAddress);
        log.Amount.ShouldBe(1_00000000);
        log.Seed.ShouldBe(seed);
        log.PoolId.ShouldBe(poolId);
        log.Period.ShouldBe(100);
        log.StakeId.ShouldNotBeNull();

        var stakeInfo = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(log.StakeId);
        stakeInfo.IsInUnstakeWindow.ShouldBeFalse();
        stakeInfo.StakeInfo.StakingPeriod.ShouldBe(110);
    }

    [Fact]
    public async Task StakeRewardsTests_Fail()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);
        SetBlockTime(10);
        var claimIds = Claim(poolId).Result.Select(c => c.ClaimId).ToList();

        var result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput());
        result.TransactionResult.Error.ShouldContain("Invalid stake input.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput()
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { }
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() }
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = new Address()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = new Hash()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test")
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = new Hash()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = HashHelper.ComputeFrom("test")
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid period.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = HashHelper.ComputeFrom("test"),
                Period = 1
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = HashHelper.ComputeFrom("test"),
                Period = 1,
                DappId = new Hash()
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid dapp id.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = HashHelper.ComputeFrom("test"),
                Period = 1,
                DappId = HashHelper.ComputeFrom("test")
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid longest release time.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = DefaultAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = HashHelper.ComputeFrom("test"),
                Period = 1,
                DappId = HashHelper.ComputeFrom("test"),
                LongestReleaseTime = 1
            }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = UserAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = poolId,
                Period = 1,
                DappId = HashHelper.ComputeFrom("test"),
                LongestReleaseTime = 1
            }
        });
        result.TransactionResult.Error.ShouldContain("Dapp id not exists.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = UserAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = poolId,
                Period = 1,
                DappId = _appId,
                LongestReleaseTime = 1
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid expiration time.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = UserAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = poolId,
                Period = 1,
                DappId = _appId,
                LongestReleaseTime = 1,
                ExpirationTime = 1
            }
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = UserAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = poolId,
                Period = 1,
                DappId = _appId,
                LongestReleaseTime = 1,
                ExpirationTime = 1
            },
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Signature expired.");

        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = new StakeInput
            {
                ClaimIds = { new Hash() },
                Account = UserAddress,
                Amount = 1000000_00000000,
                Seed = HashHelper.ComputeFrom("test"),
                PoolId = poolId,
                Period = 1,
                DappId = _appId,
                LongestReleaseTime = 1,
                ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds
            },
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        var stakeInput = new StakeInput
        {
            ClaimIds = { new Hash() },
            Account = UserAddress,
            Amount = 1000000_00000000,
            Seed = HashHelper.ComputeFrom("test"),
            PoolId = poolId,
            Period = 1,
            DappId = _appId,
            LongestReleaseTime = 1,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds
        };
        
        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = stakeInput,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, new StakeRewardsInput { StakeInput = stakeInput })
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");
        
        stakeInput = new StakeInput
        {
            ClaimIds = { HashHelper.ComputeFrom("test") },
            Account = UserAddress,
            Amount = 1000000_00000000,
            Seed = HashHelper.ComputeFrom("test"),
            PoolId = poolId,
            Period = 1,
            DappId = _appId,
            LongestReleaseTime = 1,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds
        };
        
        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = stakeInput,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, new StakeRewardsInput { StakeInput = stakeInput })
        });
        result.TransactionResult.Error.ShouldContain("Claim id not exists.");
        
        stakeInput = new StakeInput
        {
            ClaimIds = { claimIds },
            Account = UserAddress,
            Amount = 1000000_00000000,
            Seed = HashHelper.ComputeFrom("test"),
            PoolId = poolId,
            Period = 1,
            DappId = _appId,
            LongestReleaseTime = 1,
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds
        };
        
        result = await UserEcoEarnRewardsContractStub.StakeRewards.SendWithExceptionAsync(new StakeRewardsInput
        {
            StakeInput = stakeInput,
            Signature = GenerateSignature(DefaultKeyPair.PrivateKey, new StakeRewardsInput { StakeInput = stakeInput })
        });
        result.TransactionResult.Error.ShouldContain("Amount too much.");
    }

    private async Task<Hash> CreateTokensPool()
    {
        await Initialize();
        await EcoEarnRewardsContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
        await CreateToken();

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
            MinimumAddLiquidityAmount = 1,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 1,
            UnstakeWindowDuration = 100,
            ReleasePeriods = { 10, 20, 30 },
            MinimumAdditionalStakeAmount = 1
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

    private async Task<StakeInfo> Stake(Hash poolId, long tokenBalance)
    {
        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = UserAddress,
            Symbol = Symbol,
            Amount = tokenBalance
        });
        await UserTokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = tokenBalance
        });

        var result = await UserEcoEarnTokensContractStub.Stake.SendAsync(new Tokens.StakeInput
        {
            PoolId = poolId,
            Amount = tokenBalance,
            Period = 10
        });
        return GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
    }

    private async Task<List<ClaimInfo>> Claim(Hash poolId)
    {
        var result = await UserEcoEarnTokensContractStub.Claim.SendAsync(poolId);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        return GetLogEvent<Claimed>(result.TransactionResult, 1).ClaimInfos.Data.ToList();
    }

    private ByteString GenerateSignature(byte[] privateKey, List<Hash> claimIds, long amount, Address account,
        Hash seed, long expirationTime, Hash dappId)
    {
        var data = new WithdrawInput
        {
            ClaimIds = { claimIds },
            Account = account,
            Amount = amount,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = dappId
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }

    private ByteString GenerateSignature(byte[] privateKey, StakeRewardsInput input)
    {
        var data = new StakeRewardsInput
        {
            StakeInput = input.StakeInput
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
}