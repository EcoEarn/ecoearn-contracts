using AElf;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.TestAwakenContract;

public class TestAwakenContract : TestAwakenContractContainer.TestAwakenContractBase
{
    public override TokenInfo GetTokenInfo(GetTokenInfoInput input)
    {
        return new TokenInfo
        {
            Symbol = input.Symbol,
            Decimals = 8
        };
    }

    public override Int64Value Quote(QuoteInput input)
    {
        return new Int64Value
        {
            Value = 1_00000000
        };
    }

    public override GetReservesOutput GetReserves(GetReservesInput input)
    {
        return new GetReservesOutput
        {
            Results = { new ReservePairResult
            {
                SymbolPair = "ELF-SGR-1",
                ReserveA = 1,
                ReserveB = 1,
                SymbolA = "ELF",
                SymbolB = "SGR-1"
            } }
        };
    }

    public override GetTotalSupplyOutput GetTotalSupply(StringList input)
    {
        return new GetTotalSupplyOutput
        {
            Results = { new TotalSupplyResult
            {
                SymbolPair = "ELF-SGR-1",
                TotalSupply = 1
            } }
        };
    }

    public override Empty Approve(ApproveInput input)
    {
        return new Empty();
    }

    public override AddLiquidityOutput AddLiquidity(AddLiquidityInput input)
    {
        return new AddLiquidityOutput();
    }

    public override RemoveLiquidityOutput RemoveLiquidity(RemoveLiquidityInput input)
    {
        return new RemoveLiquidityOutput();
    }

    public override Empty Transfer(TransferInput input)
    {
        return new Empty();
    }

    public override Empty TransferFrom(TransferFromInput input)
    {
        return new Empty();
    }

    public override GetBalanceOutput GetBalance(GetBalanceInput input)
    {
        return new GetBalanceOutput
        {
            Balance = 1000_00000000
        };
    }
}