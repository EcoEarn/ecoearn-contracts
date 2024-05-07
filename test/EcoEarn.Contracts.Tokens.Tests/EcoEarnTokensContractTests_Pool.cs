using System.Threading.Tasks;
using AElf;
using AElf.Contracts.MultiToken;
using AElf.Cryptography;
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

        var output = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeInfo.StakeId);
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
                EndBlockNumber = blockNumber + 5,
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