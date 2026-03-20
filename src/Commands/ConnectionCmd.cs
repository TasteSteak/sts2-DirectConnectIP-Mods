using DirectConnectIP.Network;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;

namespace DirectConnectIP.Commands
{
    public class ConnectionCmd : AbstractConsoleCmd
    {
        private const string DefaultPort = "33771";

        public override string CmdName => "connect";
        public override string Args => "<ip> [port]";
        public override string Description => "通过 IP 地址直接连接到房间";
        public override bool IsNetworked => false;
        public override bool DebugOnly => false;

        public override CmdResult Process(Player issuingPlayer, string[] args)
        {
            if (args.Length < 1)
                return new CmdResult(false, "用法: connect <ip> [port]");

            var ip = args[0];
            var portStr = args.Length > 1 ? args[1] : DefaultPort;

            if (!ushort.TryParse(portStr, out ushort port))
                return new CmdResult(false, "端口号无效。");

            var connInitializer = new DirectClientConnectionInitializer(ip, port, ModEntry.Config.LocalPlayerId);
            TaskHelper.RunSafely(ConnectionService.ConnectAsync(connInitializer));

            return new CmdResult(true, "正在尝试连接...");
        }

        public override CompletionResult GetArgumentCompletions(Player player, string[] args) => new();
    }
}