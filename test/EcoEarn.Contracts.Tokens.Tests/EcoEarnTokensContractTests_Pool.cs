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
    
    [Fact]
    public async Task Test()
    {
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
        
        await EcoEarnTokensContractStub.Register.SendAsync(new RegisterInput
        {
            DappId = _appId
        });
        
        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 10000,
            Symbol = Symbol
        });
        
        var blockNumber = SimulateBlockMining().Result.Block.Height;

        var result = await EcoEarnTokensContractStub.CreateTokensPool.SendAsync(new CreateTokensPoolInput
        {
            DappId = _appId,
            Config = new TokensPoolConfig
            {
                StartBlockNumber = blockNumber,
                EndBlockNumber = blockNumber + 5,
                RewardToken = Symbol,
                StakingToken = DefaultSymbol,
                FixedBoostFactor = 1000,
                MaximumStakeDuration = 100,
                MinimalAmount = 1,
                MinimalClaimAmount = 1,
                RewardPerBlock = 10,
                ReleasePeriod = 60,
                RewardTokenContract = TokenContractAddress,
                StakeTokenContract = TokenContractAddress,
                UpdateAddress = DefaultAddress
            }
        });

        var poolId = GetLogEvent<TokensPoolCreated>(result.TransactionResult).PoolId;

        await TokenContractStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 10000,
            Symbol = DefaultSymbol
        });
        
        await TokenContractUserStub.Approve.SendAsync(new ApproveInput
        {
            Spender = EcoEarnTokensContractAddress,
            Amount = 10000,
            Symbol = DefaultSymbol
        });
        
        result = await EcoEarnTokensContractStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 100,
            Period = 30
        });
        var log = GetLogEvent<Staked>(result.TransactionResult);
        var stakeId = log.StakeInfo.StakeId;
        var stakedHeight = log.StakeInfo.StakedBlockNumber;
        
        result = await EcoEarnTokensContractUserStub.Stake.SendAsync(new StakeInput
        {
            PoolId = poolId,
            Amount = 100,
            Period = 30
        });
        log = GetLogEvent<Staked>(result.TransactionResult);
        var stakeId2 = log.StakeInfo.StakeId;
        var stakedHeight2 = log.StakeInfo.StakedBlockNumber;

        var currentHeight = SimulateBlockMining().Result.Block.Height;

        var stakeInfo = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId);
        var stakeInfo2 = await EcoEarnTokensContractStub.GetStakeInfo.CallAsync(stakeId2);
        
        var output = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeId);
        var output2 = await EcoEarnTokensContractStub.GetReward.CallAsync(stakeId2);
        
        output.Amount.ShouldBe(15);
        output2.Amount.ShouldBe(5);
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