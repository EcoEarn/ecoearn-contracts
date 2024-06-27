﻿using AElf;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Tokens;

public partial class EcoEarnTokensContract : EcoEarnTokensContractContainer.EcoEarnTokensContractBase
{
    public override Empty Initialize(InitializeInput input)
    {
        Assert(!State.Initialized.Value, "Already initialized.");
        State.GenesisContract.Value = Context.GetZeroSmartContractAddress();
        Assert(State.GenesisContract.GetContractAuthor.Call(Context.Self) == Context.Sender, "No permission.");
        Assert(input.Admin == null || !input.Admin.Value.IsNullOrEmpty(), "Invalid admin.");
        Assert(IsAddressValid(input.EcoearnPointsContract), "Invalid ecoearn points contract.");
        Assert(IsAddressValid(input.EcoearnRewardsContract), "Invalid ecoearn rewards contract.");

        State.Admin.Value = input.Admin ?? Context.Sender;
        State.EcoEarnPointsContract.Value = input.EcoearnPointsContract;
        State.EcoEarnRewardsContract.Value = input.EcoearnRewardsContract;

        Assert(input.CommissionRate >= 0, "Invalid commission rate.");
        Assert(input.Recipient == null || !input.Recipient.Value.IsNullOrEmpty(), "Invalid recipient.");
        Assert(input.MaximumPositionAmount > 0, "Invalid maximum position amount.");

        State.Config.Value = new Config
        {
            CommissionRate = input.CommissionRate,
            Recipient = input.Recipient ?? Context.Sender,
            IsRegisterRestricted = input.IsRegisterRestricted,
            MaximumPositionAmount = input.MaximumPositionAmount
        };

        State.TokenContract.Value = Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);

        State.Initialized.Value = true;

        return new Empty();
    }

    public override Empty SetAdmin(Address input)
    {
        CheckAdminPermission();
        Assert(IsAddressValid(input), "Invalid input.");

        if (State.Admin.Value == input) return new Empty();

        State.Admin.Value = input;

        Context.Fire(new AdminSet
        {
            Admin = input
        });

        return new Empty();
    }

    public override Empty SetConfig(Config input)
    {
        CheckAdminPermission();

        Assert(input != null, "Invalid input.");
        Assert(input!.CommissionRate >= 0, "Invalid commission rate.");
        Assert(IsAddressValid(input.Recipient), "Invalid recipient.");

        if (input.Equals(State.Config.Value)) return new Empty();

        State.Config.Value = input;

        Context.Fire(new ConfigSet
        {
            Config = input
        });

        return new Empty();
    }
}