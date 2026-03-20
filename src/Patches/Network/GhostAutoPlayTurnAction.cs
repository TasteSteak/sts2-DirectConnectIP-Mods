using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace DirectConnectIP.Patches.Network;

public class GhostAutoPlayTurnAction(Player ghostPlayer, int roundNumber) : GameAction
{
    public override ulong OwnerId => ghostPlayer.NetId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override async Task ExecuteAction()
    {
        var combatState = ghostPlayer.Creature.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding) return;

        var choiceContext = new GameActionPlayerChoiceContext(this);

        using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
        {
            while (!CombatManager.Instance.IsOverOrEnding)
            {
                var handPile = PileType.Hand.GetPile(ghostPlayer);
                var cardToPlay = handPile.Cards.FirstOrDefault(c => c.CanPlay());

                if (cardToPlay == null) break;

                var target = cardToPlay.TargetType switch
                {
                    TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
                    TargetType.AnyPlayer or TargetType.Self => ghostPlayer.Creature,
                    TargetType.AnyAlly => combatState.Allies.FirstOrDefault(c =>
                        c is { IsAlive: true, IsPlayer: true } && c != ghostPlayer.Creature),
                    _ => null
                };

                Log.Info($"[DirectConnectIP] Offline player {ghostPlayer.NetId} automatically plays cards: {cardToPlay.Id.Entry}");

                await cardToPlay.SpendResources();
                await CardCmd.AutoPlay(choiceContext, cardToPlay, target, skipXCapture: true);
            }
        }

        if (!CombatManager.Instance.IsPlayerReadyToEndTurn(ghostPlayer))
        {
            Log.Info($"[DirectConnectIP] Offline player {ghostPlayer.NetId} finish playing.");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(ghostPlayer, roundNumber));
        }
    }

    public override INetAction ToNetAction()
    {
        var dummyAction = new EndPlayerTurnAction(ghostPlayer, roundNumber);
        return dummyAction.ToNetAction();
    }
}