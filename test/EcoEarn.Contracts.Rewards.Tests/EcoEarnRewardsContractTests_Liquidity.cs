using System.Linq;
using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using EcoEarn.Contracts.Tokens;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Shouldly;
using Xunit;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContractTests
{
    private const string LpSymbol = "ALP ELF-SGR-1";
    
    [Fact]
    public async Task AddLiquidityAndStakeTests()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);
        SetBlockTime(10);
        var claimInfos = await Claim(poolId);
        
        poolId = await CreateLpTokensPool();

        var stakeInput = new StakeInput
        {
            ClaimIds = { claimInfos.Select(c => c.ClaimId) },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = _appId,
            PoolId = poolId,
            Period = 100,
            LongestReleaseTime = claimInfos.Last().ReleaseTime.Seconds
        };

        var input = new AddLiquidityAndStakeInput
        {
            StakeInput = stakeInput,
            TokenAMin = 10,
            TokenBMin = 10,
            Deadline = BlockTimeProvider.GetBlockTime().AddDays(1),
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, new AddLiquidityAndStakeInput
            {
                StakeInput = stakeInput,
                TokenAMin = 10,
                TokenBMin = 10
            })
        };

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 100_00000000,
            To = UserAddress,
            Symbol = DefaultSymbol
        });
        await UserTokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnRewardsContractAddress,
            Amount = 1_00000000,
            Symbol = DefaultSymbol
        });

        var result = await UserEcoEarnRewardsContractStub.AddLiquidityAndStake.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<LiquidityAdded>(result.TransactionResult);
        log.ClaimIds.Data.ShouldBe(claimInfos.Select(c => c.ClaimId));
        log.Account.ShouldBe(UserAddress);
        log.Amount.ShouldBe(1_00000000);
        log.PoolId.ShouldBe(poolId);
        log.Period.ShouldBe(100);

        var liquidityInfo = log.LiquidityInfo;
        liquidityInfo.Seed.ShouldBe(seed);
        liquidityInfo.LpAmount.ShouldBe(1_00000000);
        liquidityInfo.LpSymbol.ShouldBe(LpSymbol);
        liquidityInfo.RewardSymbol.ShouldBe(Symbol);
        liquidityInfo.TokenASymbol.ShouldBe(Symbol);
        liquidityInfo.TokenBSymbol.ShouldBe(DefaultSymbol);
        liquidityInfo.TokenAAmount.ShouldBe(1_00000000);
        liquidityInfo.TokenBAmount.ShouldBe(1_00000000);
        liquidityInfo.AddedTime.ShouldBe(BlockTimeProvider.GetBlockTime());
        liquidityInfo.DappId.ShouldBe(_appId);
        liquidityInfo.SwapAddress.ShouldBe(AwakenContractAddress);
        liquidityInfo.TokenAddress.ShouldBe(AwakenContractAddress);
        liquidityInfo.Account.ShouldBe(UserAddress);

        EcoEarnRewardsContractStub.GetLiquidityInfo.CallAsync(liquidityInfo.LiquidityId).Result.ShouldBe(liquidityInfo);

        var stakeId = await EcoEarnTokensContractStub.GetUserStakeId.CallAsync(new GetUserStakeIdInput
        {
            PoolId = poolId,
            Account = UserAddress
        });
        log.StakeId.ShouldBe(stakeId);
    }

    [Fact]
    public async Task RemoveLiquidityTests()
    {
        var seed = HashHelper.ComputeFrom("seed");
        
        var (_, liquidityInfo) = await AddLiquidity();

        var removeLiquidityInput = new RemoveLiquidityInput
        {
            LiquidityInput = new LiquidityInput
            {
                DappId = _appId,
                LiquidityIds = { liquidityInfo.LiquidityId },
                LpAmount = liquidityInfo.LpAmount,
                ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds,
                Seed = seed
            },
            TokenAMin = 1_00000000,
            TokenBMin = 1_00000000,
            Deadline = BlockTimeProvider.GetBlockTime().AddDays(1)
        };

        removeLiquidityInput.Signature = GenerateSignature(DefaultKeyPair.PrivateKey, removeLiquidityInput);

        var result = await UserEcoEarnRewardsContractStub.RemoveLiquidity.SendAsync(removeLiquidityInput);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<LiquidityRemoved>(result.TransactionResult);
        log.LiquidityIds.Data.ShouldBe(new RepeatedField<Hash>{ liquidityInfo.LiquidityId });
        log.LpAmount.ShouldBe(removeLiquidityInput.LiquidityInput.LpAmount);
        log.TokenAAmount.ShouldBe(1_00000000);
        log.TokenBAmount.ShouldBe(1_00000000);
        log.DappId.ShouldBe(_appId);
        log.Seed.ShouldBe(seed);
    }
    
    [Fact]
    public async Task StakeLiquidityTests()
    {
        var seed = HashHelper.ComputeFrom("seed");
        
        var (poolId, liquidityInfo) = await AddLiquidity();

        var stakeLiquidityInput = new StakeLiquidityInput
        {
            LiquidityInput = new LiquidityInput
            {
                DappId = _appId,
                LiquidityIds = { liquidityInfo.LiquidityId },
                LpAmount = liquidityInfo.LpAmount,
                ExpirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds,
                Seed = seed
            },
            PoolId = poolId,
            Period = 100
        };

        stakeLiquidityInput.Signature = GenerateSignature(DefaultKeyPair.PrivateKey, stakeLiquidityInput);

        var result = await UserEcoEarnRewardsContractStub.StakeLiquidity.SendAsync(stakeLiquidityInput);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var log = GetLogEvent<LiquidityStaked>(result.TransactionResult);
        log.LiquidityIds.Data.ShouldBe(new RepeatedField<Hash>{ liquidityInfo.LiquidityId });
        log.PoolId.ShouldBe(poolId);
        log.LpAmount.ShouldBe(liquidityInfo.LpAmount);
        log.Period.ShouldBe(100);
        log.Seed.ShouldBe(seed);
    }
    
    private ByteString GenerateSignature(byte[] privateKey, AddLiquidityAndStakeInput input)
    {
        var data = new AddLiquidityAndStakeInput
        {
            StakeInput = input.StakeInput,
            TokenAMin = input.TokenAMin,
            TokenBMin = input.TokenBMin
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
    
    private ByteString GenerateSignature(byte[] privateKey, RemoveLiquidityInput input)
    {
        var data = new RemoveLiquidityInput
        {
            LiquidityInput = input.LiquidityInput,
            TokenAMin = input.TokenAMin,
            TokenBMin = input.TokenBMin
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
    
    private ByteString GenerateSignature(byte[] privateKey, StakeLiquidityInput input)
    {
        var data = new StakeLiquidityInput
        {
            LiquidityInput = input.LiquidityInput,
            PoolId = input.PoolId,
            Period = input.Period
        };
        var dataHash = HashHelper.ComputeFrom(data);
        var signature = CryptoHelper.SignWithPrivateKey(privateKey, dataHash.ToByteArray());
        return ByteStringHelper.FromHexString(signature.ToHex());
    }
    
    private async Task<Hash> CreateLpTokensPool()
    {
        var blockTime = BlockTimeProvider.GetBlockTime().Seconds;

        var input = new CreateTokensPoolInput
        {
            DappId = _appId,
            StartTime = blockTime,
            EndTime = blockTime + 100000,
            RewardToken = Symbol,
            StakingToken = LpSymbol,
            FixedBoostFactor = 1,
            MaximumStakeDuration = 500000,
            MinimumAmount = 1_00000000,
            MinimumClaimAmount = 1_00000000,
            RewardPerSecond = 100_00000000,
            RewardTokenContract = TokenContractAddress,
            StakeTokenContract = AwakenContractAddress,
            SwapContract = AwakenContractAddress,
            MinimumStakeDuration = 1,
            UnlockWindowDuration = 100,
            ReleasePeriods = { 10, 20, 30 },
            MinimumAddLiquidityAmount = 1_00000000
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

    private async Task<(Hash, LiquidityInfo)> AddLiquidity()
    {
        const long tokenBalance = 5_00000000;
        var seed = HashHelper.ComputeFrom("seed");
        var expirationTime = BlockTimeProvider.GetBlockTime().AddDays(1).Seconds;

        var poolId = await CreateTokensPool();
        _ = await Stake(poolId, tokenBalance);
        SetBlockTime(10);
        var claimInfos = await Claim(poolId);
        
        poolId = await CreateLpTokensPool();

        var stakeInput = new StakeInput
        {
            ClaimIds = { claimInfos.Select(c => c.ClaimId) },
            Account = UserAddress,
            Amount = 1_00000000,
            Seed = seed,
            ExpirationTime = expirationTime,
            DappId = _appId,
            PoolId = poolId,
            Period = 100,
            LongestReleaseTime = claimInfos.Last().ReleaseTime.Seconds
        };

        var input = new AddLiquidityAndStakeInput
        {
            StakeInput = stakeInput,
            TokenAMin = 10,
            TokenBMin = 10,
            Deadline = BlockTimeProvider.GetBlockTime().AddDays(1),
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, new AddLiquidityAndStakeInput
            {
                StakeInput = stakeInput,
                TokenAMin = 10,
                TokenBMin = 10
            })
        };

        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 100_00000000,
            To = UserAddress,
            Symbol = DefaultSymbol
        });
        await UserTokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnRewardsContractAddress,
            Amount = 1_00000000,
            Symbol = DefaultSymbol
        });

        var result = await UserEcoEarnRewardsContractStub.AddLiquidityAndStake.SendAsync(input);
        result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

        var liquidityInfo = GetLogEvent<LiquidityAdded>(result.TransactionResult).LiquidityInfo;

        return (poolId, liquidityInfo);
    }
}