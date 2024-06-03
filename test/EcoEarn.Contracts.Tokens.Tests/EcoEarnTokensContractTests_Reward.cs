using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContractTests
{
    [Fact]
    public async Task ClaimTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom(1);

        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
        stakeInfo.RewardAmount.ShouldBe(0);

        SetBlockTime(1);

        var reward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeInfo.StakeId }
        });

        var addressInfo = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, addressInfo.RewardAddress);

        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var input = new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = UserAddress,
            Amount = 10,
            Seed = seed,
            ExpirationTime = expirationTime,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, stakeInfo.StakeId, 10, UserAddress, seed,
                expirationTime)
        };

        var result = await EcoEarnTokensContractUserStub.Claim.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<Claimed>(result.TransactionResult);
        log.StakeId.ShouldBe(stakeInfo.StakeId);
        log.ClaimInfo.ClaimedSymbol.ShouldBe(Symbol);
        log.ClaimInfo.ClaimedAmount.ShouldBe(input.Amount);
        log.ClaimInfo.ClaimedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        log.ClaimInfo.PoolId.ShouldBe(poolId);
        log.ClaimInfo.Account.ShouldBe(UserAddress);
        log.ClaimInfo.ClaimedBlockNumber.ShouldBe(result.TransactionResult.BlockNumber);

        var output = await EcoEarnTokensContractStub.GetClaimInfo.CallAsync(log.ClaimInfo.ClaimId);
        output.ShouldBe(log.ClaimInfo);

        var stakeOutput = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        stakeOutput.StakeInfo.RewardAmount.ShouldBe(reward.RewardInfos.First().Amount);

        SetBlockTime(1);

        var newReward = await EcoEarnTokensContractStub.GetReward.CallAsync(new GetRewardInput
        {
            StakeIds = { stakeOutput.StakeInfo.StakeId }
        });
        newReward.RewardInfos.First().Amount.ShouldBe(reward.RewardInfos.First().Amount * 2);

        var balance2 = await GetTokenBalance(Symbol, addressInfo.RewardAddress);
        balance2.ShouldBe(balance - 1_00000000 - 10);
    }

    [Fact]
    public async Task ClaimTests_Fail()
    {
        const long tokenBalance = 5_00000000;
    
        var poolId = await CreateTokensPool();
        var stakeInfo = await Stake(poolId, tokenBalance);
    
        var result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput());
        result.TransactionResult.Error.ShouldContain("Invalid stake id.");

        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid stake id.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test")
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = UserAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid account.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress
        });
        result.TransactionResult.Error.ShouldContain("Invalid amount.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Amount = 100_00000000
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Amount = 100_00000000,
            Seed = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid seed.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed")
        });
        result.TransactionResult.Error.ShouldContain("Invalid expiration time.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(-1).Seconds
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = HashHelper.ComputeFrom("test"),
            Account = DefaultAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(-1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Stake info not exists.");
        
        result = await EcoEarnTokensContractStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = DefaultAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(-1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
        
        SetBlockTime(-1);
        
        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = UserAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(-1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Pool not start.");
        
        SetBlockTime(2);
        
        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = UserAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(-1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Signature expired.");
        
        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = UserAddress,
            Amount = 100_00000000,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Amount too much.");
        
        result = await EcoEarnTokensContractUserStub.Claim.SendWithExceptionAsync(new ClaimInput
        {
            StakeId = stakeInfo.StakeId,
            Account = UserAddress,
            Amount = 100,
            Seed = HashHelper.ComputeFrom("seed"),
            ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds,
            Signature = Hash.Empty.ToByteString()
        });
        result.TransactionResult.Error.ShouldContain("Invalid signature.");
    }

    [Fact]
    public async Task RecoverTokenTests()
    {
        var poolId = await CreateTokensPool();

        var address = await EcoEarnTokensContractStub.GetPoolAddressInfo.CallAsync(poolId);
        var balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(10000000_00000000);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(0);

        SetBlockTime(86401);

        var result = await EcoEarnTokensContractStub.RecoverToken.SendAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Recipient = UserAddress,
            Token = Symbol
        });
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<TokenRecovered>(result.TransactionResult);
        log.Amount.ShouldBe(10000000_00000000);
        log.PoolId.ShouldBe(poolId);
        log.Account.ShouldBe(UserAddress);
        log.Token.ShouldBe(Symbol);

        balance = await GetTokenBalance(Symbol, address.RewardAddress);
        balance.ShouldBe(0);
        balance = await GetTokenBalance(Symbol, UserAddress);
        balance.ShouldBe(10000000_00000000);
    }

    [Fact]
    public async Task RecoverTokenTests_Fail()
    {
        var poolId = await CreateTokensPool();

        var result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput());
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = new Hash()
        });
        result.TransactionResult.Error.ShouldContain("Invalid pool id.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1)
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = HashHelper.ComputeFrom(1),
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not exists.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Pool not closed.");

        SetBlockTime(86401);

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "TEST"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = "ELF"
        });
        result.TransactionResult.Error.ShouldContain("Invalid token.");

        result = await EcoEarnTokensContractStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
            Recipient = new Address()
        });
        result.TransactionResult.Error.ShouldContain("Invalid recipient.");

        result = await EcoEarnTokensContractUserStub.RecoverToken.SendWithExceptionAsync(new RecoverTokenInput
        {
            PoolId = poolId,
            Token = Symbol,
        });
        result.TransactionResult.Error.ShouldContain("No permission.");
    }

    private async Task<Hash> CreateTokensPoolWithHighLimitation()
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
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
            FixedBoostFactor = 10000,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1000_00000000,
            RewardPerSecond = 100_00000000,
            ReleasePeriod = 10,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = TokenContractAddress,
            MinimumStakeDuration = 86400,
            UnlockWindowDuration = 100
        };
        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(input);
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }

    private async Task<Hash> CreateTokensPoolAwayFromStart()
    {
        var admin = await EcoEarnTokensContractStub.GetAdmin.CallAsync(new Empty());
        if (admin == new Address())
        {
            await Register();
            await CreateToken();
        }

        var blockTime = BlockTimeProvider.GetBlockTime().AddSeconds(1000).Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 10,
            RewardToken = Symbol,
            StakingToken = Symbol,
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
        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
    }

    private ByteString GenerateSignature(byte[] privateKey, Hash stakeId, long amount, Address account, Hash seed,
        long expirationTime)
    {
        var data = new ClaimInput
        {
            StakeId = stakeId,
            Account = account,
            Amount = amount,
            Seed = seed,
            ExpirationTime = expirationTime
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
}