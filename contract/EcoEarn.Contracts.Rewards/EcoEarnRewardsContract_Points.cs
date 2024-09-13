using System.Collections.Generic;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Points.Contracts.Point;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    #region public

    public override Empty SetPointsContractConfig(SetPointsContractConfigInput input)
    {
        CheckAdminPermission();
        
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        Assert(IsAddressValid(input.Admin), "Invalid admin.");

        var config = new PointsContractConfig
        {
            DappId = input.DappId,
            Admin = input.Admin
        };

        if (State.PointsContractConfig.Value != null && State.PointsContractConfig.Value.Equals(config))
            return new Empty();

        State.PointsContractConfig.Value = config;

        Context.Fire(new PointsContractConfigSet
        {
            Config = config,
            PointsContract = State.PointsContract.Value
        });

        return new Empty();
    }

    public override Empty Join(Empty input)
    {
        Assert(!State.JoinRecord[Context.Sender], "Already joined.");

        Join(Context.Sender);

        return new Empty();
    }

    public override Empty JoinFor(Address input)
    {
        Assert(
            Context.Sender == State.EcoEarnTokensContract.Value || Context.Sender == State.EcoEarnPointsContract.Value,
            "No permission.");
        
        Assert(IsAddressValid(input), "Invalid input.");

        Join(input);

        return new Empty();
    }

    public override Empty AcceptReferral(AcceptReferralInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsAddressValid(input!.Referrer) && State.JoinRecord[input.Referrer], "Invalid referrer.");
        Assert(!State.JoinRecord[Context.Sender], "Already joined.");

        State.JoinRecord[Context.Sender] = true;

        var config = GetPointsContractConfig();

        State.PointsContract.AcceptReferral.Send(new global::Points.Contracts.Point.AcceptReferralInput
        {
            DappId = config!.DappId,
            Referrer = input.Referrer,
            Invitee = Context.Sender
        });

        Context.Fire(new ReferralAccepted
        {
            Invitee = Context.Sender,
            Referrer = input.Referrer
        });

        return new Empty();
    }

    public override Empty BatchSettle(BatchSettleInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsStringValid(input!.ActionName), "Invalid action name.");
        Assert(input.UserPointsList != null && input.UserPointsList.Count > 0, "Invalid user points list.");

        var config = GetPointsContractConfig();
        Assert(Context.Sender == config.Admin, "No permission.");

        var userPointsList = new List<global::Points.Contracts.Point.UserPoints>();
        foreach (var userPoints in input.UserPointsList!)
        {
            Join(userPoints.UserAddress);
            userPointsList.Add(new global::Points.Contracts.Point.UserPoints
            {
                UserAddress = userPoints.UserAddress,
                UserPointsValue = userPoints.UserPointsValue
            });
        }

        State.PointsContract.BatchSettle.Send(new global::Points.Contracts.Point.BatchSettleInput
        {
            ActionName = input.ActionName,
            DappId = config.DappId,
            UserPointsList = { userPointsList }
        });

        return new Empty();
    }

    #endregion

    #region private

    private string GetOfficialDomain(PointsContractConfig config)
    {
        var getDappInformationOutput = State.PointsContract.GetDappInformation.Call(new GetDappInformationInput
        {
            DappId = config.DappId
        });
        return getDappInformationOutput?.DappInfo.OfficialDomain;
    }

    private void Join(Address registrant, string domain = null)
    {
        if (State.JoinRecord[registrant]) return;

        var config = GetPointsContractConfig();

        domain ??= GetOfficialDomain(config);

        State.JoinRecord[registrant] = true;

        State.PointsContract.Join.Send(new JoinInput
        {
            Registrant = registrant,
            DappId = config.DappId,
            Domain = domain
        });

        Context.Fire(new Joined
        {
            Domain = domain,
            Registrant = registrant
        });
    }

    private PointsContractConfig GetPointsContractConfig()
    {
        var config = State.PointsContractConfig.Value;
        Assert(config != null, "Points contract config not set.");

        return config;
    }

    #endregion
}