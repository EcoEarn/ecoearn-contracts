using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
using AElf.CSharp.Core.Extension;
using AElf.Types;
using EcoEarn.Contracts.Points;
using Google.Protobuf;
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
    public async Task Test()
    {
        await InitializeContract();

        var pointsPoolId = await CreatePointsPool();
        var tokensPoolId = await CreateTokensPool();
        var pointsPoolInfo = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(pointsPoolId);
        var tokensPoolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(tokensPoolId);

        // claim in points pool
        var seed = HashHelper.ComputeFrom("seed");
        var input = new ClaimInput
        {
            PoolId = pointsPoolId,
            Account = DefaultAddress,
            Amount = 100,
            Seed = seed,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, pointsPoolId, 100, DefaultAddress, seed)
        };
        var result = await EcoEarnPointsContractStub.Claim.SendAsync(input);
        var claimInfo = GetLogEvent<Points.Claimed>(result.TransactionResult).ClaimInfo;
        
        // early stake
        result = await EcoEarnPointsContractStub.EarlyStake.SendAsync(new Points.EarlyStakeInput
        {
            PoolId = tokensPoolId,
            Period = 10,
            ClaimIds = { claimInfo.ClaimId }
        });
        var stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.BoostedAmount.ShouldBe(200);
        stakeInfo.RewardAmount.ShouldBe(0);
        stakeInfo.ClaimedAmount.ShouldBe(0);

        var output = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output.Amount.ShouldBe(10);
        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            To = UserAddress,
            Symbol = Symbol,
            Amount = 100
        });
        await TokenContractUserStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Symbol = Symbol,
            Amount = 100
        });
        result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
        {
            PoolId = tokensPoolId,
            Amount = 100,
            Period = 10
        });
        var stakeInfo2 = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo2.BoostedAmount.ShouldBe(200);
        stakeInfo2.RewardAmount.ShouldBe(0);
        stakeInfo2.ClaimedAmount.ShouldBe(0);

        var output2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output2.Amount.ShouldBe(35);
        var output3 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        output3.Amount.ShouldBe(5);

        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = tokensPoolId,
            Amount = 0,
            Period = 10
        });
        
        stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.BoostedAmount.ShouldBe(300);
        stakeInfo.RewardAmount.ShouldBe(35);
        stakeInfo.ClaimedAmount.ShouldBe(0);
        
        var output4 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output4.Amount.ShouldBe(41);
        var output5 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        output5.Amount.ShouldBe(9);
        
        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = tokensPoolId,
            Amount = 0,
            Period = 50
        });
        
        stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.BoostedAmount.ShouldBe(800);
        stakeInfo.RewardAmount.ShouldBe(41);
        stakeInfo.ClaimedAmount.ShouldBe(0);
        
        var output6 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output6.Amount.ShouldBe(49);
        var output7 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        output7.Amount.ShouldBe(11);

        result = await EcoEarnTokensContractStub.Claim.SendAsync(stakeInfo.StakeId);
        var stakeInfo3 = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        stakeInfo3.BoostedAmount.ShouldBe(800);
        stakeInfo3.RewardAmount.ShouldBe(0);
        stakeInfo3.ClaimedAmount.ShouldBe(49);
        var output8 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output8.Amount.ShouldBe(8);
        var output9 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        output9.Amount.ShouldBe(13);
        
        await EcoEarnTokensContractStub.Claim.SendAsync(stakeInfo.StakeId);
        var output10 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output10.Amount.ShouldBe(8);
        var output11 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo2.StakeId);
        output11.Amount.ShouldBe(15);
    }
    
    [Fact]
    public async Task Test2()
    {
        await InitializeContract();

        var pointsPoolId = await CreatePointsPool();
        var tokensPoolId = await CreateTokensPool();
        var pointsPoolInfo = await EcoEarnPointsContractStub.GetPoolInfo.CallAsync(pointsPoolId);
        var tokensPoolInfo = await EcoEarnTokensContractStub.GetPoolInfo.CallAsync(tokensPoolId);

        // claim in points pool
        var seed = HashHelper.ComputeFrom("seed");
        var input = new ClaimInput
        {
            PoolId = pointsPoolId,
            Account = DefaultAddress,
            Amount = 100,
            Seed = seed,
            Signature = GenerateSignature(DefaultAccount.KeyPair.PrivateKey, pointsPoolId, 100, DefaultAddress, seed)
        };
        var result = await EcoEarnPointsContractStub.Claim.SendAsync(input);
        var claimInfo = GetLogEvent<Points.Claimed>(result.TransactionResult).ClaimInfo;
        
        // early stake
        result = await EcoEarnPointsContractStub.EarlyStake.SendAsync(new Points.EarlyStakeInput
        {
            PoolId = tokensPoolId,
            Period = 1,
            ClaimIds = { claimInfo.ClaimId }
        });
        var stakeInfo = GetLogEvent<Staked>(result.TransactionResult).StakeInfo;
        stakeInfo.BoostedAmount.ShouldBe(110);
        stakeInfo.RewardAmount.ShouldBe(0);
        stakeInfo.ClaimedAmount.ShouldBe(0);

        var output = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output.Amount.ShouldBe(9);

        BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddDays(1));
        
        result = await EcoEarnTokensContractStub.UpdateStakeInfo.SendAsync(new UpdateStakeInfoInput
        {
            StakeIds = { stakeInfo.StakeId }
        });
        var stakeInfo2 = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeInfo.StakeId);
        var output2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
        output2.Amount.ShouldBe(9);
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

    private async Task InitializeContract()
    {
        await PointsContractStub.Initialize.SendAsync(new TestPointsContract.InitializeInput
        {
            PointsName = PointsName
        });
        await EcoEarnPointsContractStub.Initialize.SendAsync(new Points.InitializeInput
        {
            PointsContract = PointsContractAddress,
            EcoearnTokensContract = EcoEarnTokensContractAddress
        });
        await EcoEarnTokensContractStub.Initialize.SendAsync(new InitializeInput
        {
            EcoearnPointsContract = EcoEarnPointsContractAddress,
            CommissionRate = 0
        });
        await TokenContractStub.Transfer.SendAsync(new TransferInput
        {
            Amount = 1000,
            Symbol = DefaultSymbol,
            To = UserAddress
        });
        
        await CreateToken();
    }

    private async Task<Hash> CreatePointsPool()
    {
        await EcoEarnPointsContractStub.Register.SendAsync(new Points.RegisterInput()
        {
            DappId = _appId
        });
        
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
    
    private async Task<Hash> CreateTokensPool()
    {
        await EcoEarnTokensContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
        
        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 10000,
            Symbol = DefaultSymbol
        });
        
        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 20,
                RewardToken = DefaultSymbol,
                StakingToken = Symbol,
                FixedBoostFactor = 1000,
                MaximumStakeDuration = 100,
                MinimumAmount = 1,
                MinimumClaimAmount = 1,
                RewardPerBlock = 10,
                ReleasePeriod = 60,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                UpdateAddress = DefaultAddress
            }
        });

        return GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;
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
}