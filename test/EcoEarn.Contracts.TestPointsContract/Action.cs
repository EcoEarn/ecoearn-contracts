using AElf;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.TestPointsContract;

public class TestPointsContract : TestPointsContractContainer.TestPointsContractBase
{
    public override Empty Initialize(InitializeInput input)
    {
        State.Admin.Value = Context.Sender;
        State.PointsName.Value = input.PointsName;
        return new Empty();
    }

    public override GetDappInformationOutput GetDappInformation(GetDappInformationInput input)
    {
        if (input.DappId == HashHelper.ComputeFrom("test")) return new GetDappInformationOutput();
        if (input.DappId == HashHelper.ComputeFrom("ecoearn")) return new GetDappInformationOutput
        {
            DappInfo = new DappInfo
            {
                DappAdmin = State.Admin.Value,
                DappsPointRules = new PointsRuleList
                {
                    PointsRules =
                    {
                        new PointsRule
                        {
                            PointName = State.PointsName.Value
                        }
                    }
                },
                OfficialDomain = "domain"
            }
        };
        return new GetDappInformationOutput
        {
            DappInfo = new DappInfo
            {
                DappAdmin = State.Admin.Value,
                DappsPointRules = new PointsRuleList
                {
                    PointsRules =
                    {
                        new PointsRule
                        {
                            PointName = State.PointsName.Value
                        }
                    }
                }
            }
        };
    }

    public override PointInfo GetPoint(GetPointInput input)
    {
        if (input.PointsName == "Test") return new PointInfo();
        if (input.PointsName == "EcoEarn")
        {
            return new PointInfo
            {
                TokenName = "EcoEarn",
                Decimals = 0
            };
        }
        return new PointInfo
        {
            TokenName = "test",
            Decimals = 0
        };
    }

    public override Empty Join(JoinInput input)
    {
        return new Empty();
    }

    public override Empty AcceptReferral(AcceptReferralInput input)
    {
        return new Empty();
    }

    public override Empty BatchSettle(BatchSettleInput input)
    {
        return new Empty();
    }
}