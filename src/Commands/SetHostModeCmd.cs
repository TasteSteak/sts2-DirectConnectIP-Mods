#nullable enable
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace DirectConnectIP.Commands
{
    public class SetHostModeCmd : AbstractConsoleCmd
    {
        public override string CmdName => "sethostmode";
        public override string Args => "<steam | enet | ip>";
        public override string Description => "设置多人主机模式：steam（默认）或 enet（IP直连）";
        public override bool IsNetworked => false;
        public override bool DebugOnly => false;

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            if (args.Length == 0)
            {
                return new CmdResult(false, $"当前模式: {HostModeSettings.CurrentMode}. 用法: {CmdName} <steam | enet | ip>");
            }

            var modeStr = args[0].ToLowerInvariant();
            switch (modeStr)
            {
                case "steam":
                    HostModeSettings.CurrentMode = HostMode.Steam;
                    return new CmdResult(true, "主机模式已切换为 Steam (默认)");
                case "enet" or "ip":
                    HostModeSettings.CurrentMode = HostMode.ENet;
                    return new CmdResult(true, "主机模式已切换为 ENet (IP直连)");
                default:
                    return new CmdResult(false, $"无效模式 '{args[0]}'，请使用 steam 或 enet");
            }
        }

        public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
        {
            return args.Length == 1 ? CompleteArgument(["steam", "enet"], args, args[0]) : base.GetArgumentCompletions(player, args);
        }
    }
}