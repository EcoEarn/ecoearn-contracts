using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Points;

public partial class EcoEarnPointsContractTests
{
    [Fact]
    public async Task UpdateSnapshotTests()
    {
        var root = HashHelper.ComputeFrom(1);

        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.UpdateSnapshot.SendAsync(new UpdateSnapshotInput
        {
            PoolId = poolId,
            MerkleTreeRoot = root
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<SnapshotUpdated>(result.TransactionResult);
        log.PoolId.ShouldBe(poolId);
        log.MerkleTreeRoot.ShouldBe(root);
        log.UpdateBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

        var output = await EcoEarnPointsContractStub.GetSnapshot.CallAsync(new GetSnapshotInput
        {
            PoolId = poolId,
            BlockNumber = log.UpdateBlockNumber
        });
        output.PoolId.ShouldBe(poolId);
        output.BlockNumber.ShouldBe(log.UpdateBlockNumber);
        output.MerkleTreeRoot.ShouldBe(root);
    }

    [Fact]
    public async Task UpdateSnapshotTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId
            });
        result.TransactionResult.Error.ShouldContain("Invalid merkle tree root.");

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = new Hash()
            });
        result.TransactionResult.Error.ShouldContain("Invalid merkle tree root.");

        result = await EcoEarnPointsContractUserStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("No permission.");

        await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

        result = await EcoEarnPointsContractStub.UpdateSnapshot.SendWithExceptionAsync(
            new UpdateSnapshotInput
            {
                PoolId = poolId,
                MerkleTreeRoot = HashHelper.ComputeFrom(1)
            });
        result.TransactionResult.Error.ShouldContain("Pool disabled.");
    }

    [Fact]
    public async Task ClaimTests()
    {
        var seed = HashHelper.ComputeFrom(1);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var input = new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed)
        };

        var result = await EcoEarnPointsContractUserStub.Claim.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult);
        log.ClaimInfo.ClaimId.ShouldNotBeNull();
        log.ClaimInfo.PoolId.ShouldBe(poolId);
        log.ClaimInfo.ClaimedAmount.ShouldBeGreaterThan(0);
        log.ClaimInfo.ClaimedAmount.ShouldBeLessThan(100);
        log.ClaimInfo.ClaimedSymbol.ShouldBe(Symbol);
        log.ClaimInfo.ClaimedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);
        log.ClaimInfo.ClaimedTime.ShouldNotBeNull();
        log.ClaimInfo.UnlockTime.ShouldBe(log.ClaimInfo.ClaimedTime.AddSeconds(10));
        log.ClaimInfo.WithdrawTime.ShouldBeNull();
        log.ClaimInfo.Account.ShouldBe(UserAddress);

        var output = await EcoEarnPointsContractStub.GetClaimInfo.CallAsync(log.ClaimInfo.ClaimId);
        output.ShouldBe(log.ClaimInfo);
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        var seed = HashHelper.ComputeFrom(1);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput());
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = ByteString.Empty
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = HashHelper.ComputeFrom(1)
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            Signature = Hash.Empty.ToByteString(),
            PoolId = poolId
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");

        await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            PoolId = poolId,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 1, UserAddress, seed)
        });

        result = await EcoEarnPointsContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            Account = UserAddress,
            Amount = 1,
            Seed = seed,
            PoolId = poolId,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 1, UserAddress, seed)
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");
    }

    [Fact]
    public async Task WithdrawTests()
    {
        var seed1 = HashHelper.ComputeFrom(1);
        var seed2 = HashHelper.ComputeFrom(2);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        var result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed1,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed1)
        });
        var claimId1 = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo.ClaimId;

        result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed2,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed2)
        });
        var claimId2 = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo.ClaimId;

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(20));

        result = await EcoEarnPointsContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimId1, claimId2 }
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Withdrawn>(result.TransactionResult);
        log.ClaimInfos.Data.Count.ShouldBe(2);

        var info = log.ClaimInfos.Data.First();
        info.WithdrawTime.ShouldBe(info.ClaimedTime.AddSeconds(20));

        info = log.ClaimInfos.Data.Last();
        info.WithdrawTime.ShouldBe(info.ClaimedTime.AddSeconds(20));

        var output = await EcoEarnPointsContractStub.GetClaimInfo.CallAsync(info.ClaimId);
        output.WithdrawTime.ShouldBe(info.WithdrawTime);

        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(info.ClaimedAmount + info.ClaimedAmount);
    }

    [Fact]
    public async Task WithdrawTests_Fail()
    {
        var seed1 = HashHelper.ComputeFrom(1);
        var seed2 = HashHelper.ComputeFrom(2);
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed1,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed1)
        });
        var claimId1 = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo.ClaimId;

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(5));

        result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed2,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed2)
        });
        var claimId2 = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo.ClaimId;

        result = await EcoEarnPointsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput());
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await EcoEarnPointsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");

        result = await EcoEarnPointsContractStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimId1 }
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnPointsContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimId1 }
        });
        result.TransactionResult.Error.ShouldContain("Not unlock yet.");

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(5));

        result = await EcoEarnPointsContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimId1, claimId2 }
        });
        result.TransactionResult.Error.ShouldContain("Not unlock yet.");

        await EcoEarnPointsContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimId1 }
        });

        result = await EcoEarnPointsContractUserStub.Withdraw.SendWithExceptionAsync(new WithdrawInput
        {
            ClaimIds = { claimId1, claimId2 }
        });
        result.TransactionResult.Error.ShouldContain("Already Withdrawn.");
    }

    [Fact]
    public async Task RecoverTokenTests()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        {
            await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

            var address = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(poolId);
            var balance = await GetTokenBalance(Symbol, address);
            balance.ShouldBe(1000);
            balance = await GetTokenBalance(Symbol, UserAddress);
            balance.ShouldBe(0);

            var result = await EcoEarnPointsContractStub.RecoverToken.SendAsync(new RecoverTokenInput
            {
                PoolId = poolId,
                Recipient = UserAddress,
                Token = Symbol
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<TokenRecovered>(result.TransactionResult);
            log.Amount.ShouldBe(1000);
            log.PoolId.ShouldBe(poolId);
            log.Account.ShouldBe(UserAddress);
            log.Token.ShouldBe(Symbol);

            balance = await GetTokenBalance(Symbol, address);
            balance.ShouldBe(0);
            balance = await GetTokenBalance(Symbol, UserAddress);
            balance.ShouldBe(1000);
        }

        {
            await EcoEarnPointsContractStub.RestartPointsPool.SendAsync(new RestartPointsPoolInput
            {
                PoolId = poolId,
                Config = new PointsPoolConfig
                {
                    StartBlockNumber = 100,
                    EndBlockNumber = 200,
                    RewardPerBlock = 10,
                    RewardToken = Symbol,
                    UpdateAddress = DefaultAddress,
                    ReleasePeriod = 10
                }
            });
            await EcoEarnPointsContractStub.ClosePointsPool.SendAsync(poolId);

            var address = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(poolId);
            var balance = await GetTokenBalance(Symbol, address);
            balance.ShouldBe(1000);
            balance = await GetTokenBalance(Symbol, DefaultAddress);
            balance.ShouldBe(8000);

            var result = await EcoEarnPointsContractStub.RecoverToken.SendAsync(new RecoverTokenInput
            {
                PoolId = poolId,
                Token = Symbol
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var log = GetLogEvent<TokenRecovered>(result.TransactionResult);
            log.Amount.ShouldBe(1000);
            log.PoolId.ShouldBe(poolId);
            log.Account.ShouldBe(DefaultAddress);
            log.Token.ShouldBe(Symbol);

            balance = await GetTokenBalance(Symbol, address);
            balance.ShouldBe(0);
            balance = await GetTokenBalance(Symbol, DefaultAddress);
            balance.ShouldBe(9000);
        }
    }

    [Fact]
    public async Task RecoverTokenTests_Fail()
    {
        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1)
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1),
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "ELF"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnPointsContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        result = await EcoEarnPointsContractUserStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    [Fact]
    public async Task EarlyStakeTests()
    {
        var seed = HashHelper.ComputeFrom(1);

        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();

        var result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed)
        });
        var claimInfo = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo;
        claimInfo.EarlyStakeTime.ShouldBeNull();
        claimInfo.StakeId.ShouldBeNull();

        var userAddress = await EcoEarnPointsContractStub.GetPoolAddress.CallAsync(HashHelper.ComputeFrom(UserAddress));
        var balance = await GetTokenBalance(Symbol, userAddress);
        balance.ShouldBe(100 - 100 * 100 / 10000);

        var tokensPoolId = await CreateTokensPool(Symbol);

        var input = new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            Period = 1,
            ClaimIds = { claimInfo.ClaimId }
        };

        result = await EcoEarnPointsContractUserStub.EarlyStake.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<EarlyStaked>(result.TransactionResult);
        log.Amount.ShouldBe(100 - 100 * 100 / 10000);
        log.PoolId.ShouldBe(tokensPoolId);
        log.Period.ShouldBe(1);
        var info = log.ClaimInfos.Data.First();
        info.EarlyStakeTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        var stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        info.StakeId.ShouldBe(stakeInfo.StakeId);

        balance = await GetTokenBalance(Symbol, userAddress);
        balance.ShouldBe(0);

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddDays(10));

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(tokensPoolId);

        balance = await GetTokenBalance(Symbol, userAddress);
        balance.ShouldBe(100 - 100 * 100 / 10000);

        await EcoEarnPointsContractUserStub.EarlyStake.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(100));
        await EcoEarnTokensContractUserStub.Unlock.SendAsync(tokensPoolId);

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(100));

        await EcoEarnPointsContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimInfo.ClaimId }
        });
        balance = await GetTokenBalance(Symbol, userAddress);
        balance.ShouldBe(0);

        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(100 - 100 * 100 / 10000);
    }

    [Fact]
    public async Task EarlyStakeTests_Fail()
    {
        var seed = HashHelper.ComputeFrom(1);

        await Initialize();

        await Register();
        var poolId = await CreatePointsPool();
        var tokensPoolId = await CreateTokensPool(DefaultSymbol);

        var result = await EcoEarnPointsContractUserStub.Claim.SendAsync(new ClaimInput
        {
            PoolId = poolId,
            Account = UserAddress,
            Amount = 100,
            Seed = seed,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, poolId, 100, UserAddress, seed)
        });
        var claimId = GetLogEvent<Claimed>(result.TransactionResult).ClaimInfo.ClaimId;

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = HashHelper.ComputeFrom("test"),
            ClaimIds = { new Hash() }
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim ids.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            ClaimIds = { new Hash() },
            Period = -1
        });
        result.TransactionResult.Error.ShouldContain("Invalid period.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            ClaimIds = { new Hash() },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Invalid claim id.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            ClaimIds = { HashHelper.ComputeFrom("test") },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Claim info not exists.");

        result = await EcoEarnPointsContractStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            ClaimIds = { claimId },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("No permission.");

        result = await EcoEarnPointsContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = tokensPoolId,
            ClaimIds = { claimId },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Token not matched.");

        var newTokensPoolId = await CreateTokensPool(Symbol);

        await EcoEarnPointsContractUserStub.EarlyStake.SendAsync(new EarlyStakeInput
        {
            PoolId = newTokensPoolId,
            ClaimIds = { claimId },
            Period = 1
        });

        result = await EcoEarnPointsContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = newTokensPoolId,
            ClaimIds = { claimId },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Not unlocked.");

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddSeconds(11));

        await EcoEarnTokensContractUserStub.Unlock.SendAsync(newTokensPoolId);

        await EcoEarnPointsContractUserStub.Withdraw.SendAsync(new WithdrawInput
        {
            ClaimIds = { claimId }
        });

        result = await EcoEarnPointsContractUserStub.EarlyStake.SendWithExceptionAsync(new EarlyStakeInput
        {
            PoolId = newTokensPoolId,
            ClaimIds = { claimId },
            Period = 1
        });
        result.TransactionResult.Error.ShouldContain("Already withdrawn.");
    }

    private ByteString GenerateSignature(byte[] privateKey, Hash poolId, long amount, Address account, Hash seed)
    {
        var data = new ClaimInput
        {
            PoolId = poolId,
            Account = account,
            Amount = amount,
            Seed = seed
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }

    private async Task<Hash> CreateTokensPool(string stakingToken)
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await EcoEarnTokensContractStub.Initialize.SendAsync(new Tokens.InitializeInput
            {
                EcoearnPointsContract = EcoEarnPointsContractAddress
            });
            await EcoEarnTokensContractStub.Register.SendAsync(new Tokens.RegisterInput
            {
                DappId = _appId
            });
        }

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = 1000
        });

        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 10,
                RewardToken = Symbol,
                StakingToken = stakingToken,
                FixedBoostFactor = 0,
                MaximumStakeDuration = 10,
                MinimumAmount = 1,
                MinimumClaimAmount = 1,
                RewardPerBlock = 100,
                ReleasePeriod = 10,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                UpdateAddress = DefaultAddress,
                MinimumStakeDuration = 1
            }
        });
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }
}