using AElf;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace EcoEarn.Contracts.Rewards;

public partial class EcoEarnRewardsContract
{
    #region public

    public override Empty Register(RegisterInput input)
    {
        CheckInitialized();

        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        Assert(input.Admin == null || !input.Admin.Value.IsNullOrEmpty(), "Invalid admin.");
        Assert(IsAddressValid(input.UpdateAddress), "Invalid update address.");
        Assert(State.DappInfoMap[input.DappId] == null, "Dapp registered.");

        var dappInfo = State.EcoEarnPointsContract.GetDappInfo.Call(input.DappId);
        Assert(dappInfo.Admin == Context.Sender, "No permission to register.");

        var info = new DappInfo
        {
            DappId = input.DappId,
            Admin = input.Admin ?? Context.Sender,
            Config = new DappConfig
            {
                UpdateAddress = input.UpdateAddress
            }
        };

        State.DappInfoMap[input.DappId] = info;

        Context.Fire(new Registered
        {
            DappId = info.DappId,
            Admin = info.Admin
        });

        return new Empty();
    }

    public override Empty SetDappAdmin(SetDappAdminInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");

        var dappInfo = State.DappInfoMap[input.DappId];
        Assert(dappInfo != null, "Dapp not exists.");
        Assert(dappInfo!.Admin == Context.Sender, "No permission.");

        Assert(IsAddressValid(input.Admin), "Invalid admin.");

        if (input.Admin == dappInfo.Admin) return new Empty();

        dappInfo.Admin = input.Admin;

        Context.Fire(new DappAdminSet
        {
            DappId = input.DappId,
            Admin = input.Admin
        });

        return new Empty();
    }

    public override Empty SetDappConfig(SetDappConfigInput input)
    {
        Assert(input != null, "Invalid input.");
        Assert(IsHashValid(input!.DappId), "Invalid dapp id.");
        Assert(input.Config != null && IsAddressValid(input.Config.UpdateAddress), "Invalid update address");
        
        CheckDAppAdminPermission(input.DappId);

        if (input.Config!.Equals(State.DappInfoMap[input.DappId].Config)) return new Empty();

        State.DappInfoMap[input.DappId].Config = input.Config;

        Context.Fire(new DappConfigSet
        {
            DappId = input.DappId,
            Config = input.Config
        });

        return new Empty();
    }

    #endregion

    #region private

    private Address CalculateUserAddress(Hash dappId, Address account)
    {
        return Context.ConvertVirtualAddressToContractAddress(
            HashHelper.ConcatAndCompute(dappId, HashHelper.ComputeFrom(account)));
    }

    #endregion
}