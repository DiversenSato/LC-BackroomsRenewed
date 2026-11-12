namespace VELDDev.BackroomsRenewed.Patches;

[HarmonyPatch(typeof(ShipTeleporter))]
public class ShipTeleporter_Patches
{
    /// <summary>
    /// beamUpPlayer is a coroutine, so a plain postfix runs when the beam *starts* and vanilla still pulls
    /// the player onto the ship ~3s later. Wrap the enumerator and only act once vanilla is done.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(nameof(ShipTeleporter.beamUpPlayer))]
    public static IEnumerator BeamUpPlayerToBackrooms(IEnumerator __result, ShipTeleporter __instance)
    {
        // Captured on the same frame vanilla captures it; the radar target may change during the beam.
        var teleportedPlayer = StartOfRound.Instance.mapScreen.targetedPlayer;
        var redirectedToEnemy = teleportedPlayer != null && teleportedPlayer.redirectToEnemy != null;

        while (__result.MoveNext())
            yield return __result.Current;

        // Only handle regular teleporter, not inverse
        if (__instance.isInverseTeleporter)
            yield break;

        if (!SyncedConfig.Instance.TeleportOnShipTP || !Backrooms.Instance)
            yield break;

        // beamUpPlayer runs on every client: only the teleported player's own client rolls,
        // otherwise each client rolls separately and sends its own teleport request.
        if (teleportedPlayer == null || teleportedPlayer != GameNetworkManager.Instance.localPlayerController)
            yield break;

        if (redirectedToEnemy || teleportedPlayer.isPlayerDead)
            yield break;

        if (Backrooms.Instance.PlayersInBackrooms.Contains(teleportedPlayer.playerClientId))
            yield break;

        bool sendToTheBackrooms = CheckTeleportChance(
            teleportedPlayer,
            FairRandomizerEvent.ShipTP,
            SyncedConfig.Instance.TeleportationOddsOnShipTP
        );

        if (sendToTheBackrooms)
        {
            Backrooms.Instance.TeleportPlayerToBackrooms(teleportedPlayer, SyncedConfig.Instance.DropHeldItemsOnTeleport);
        }
    }
    
    [HarmonyPostfix, HarmonyPatch(nameof(ShipTeleporter.TeleportPlayerOutWithInverseTeleporter))]
    public static void InverseTeleporterToBackrooms(ShipTeleporter __instance, int playerObj)
    {
        if (!SyncedConfig.Instance.TeleportOnShipRevTP)
            return;

        // Get the player that was teleported by the inverse teleporter
        if (playerObj < 0 || playerObj >= StartOfRound.Instance.allPlayerScripts.Length)
            return;

        var teleportedPlayer = StartOfRound.Instance.allPlayerScripts[playerObj];
        if (teleportedPlayer == null || !Backrooms.Instance)
            return;

        // Runs on every client: only the teleported player's own client rolls.
        if (teleportedPlayer != GameNetworkManager.Instance.localPlayerController)
            return;

        bool sendToTheBackrooms = CheckTeleportChance(
            teleportedPlayer,
            FairRandomizerEvent.ShipRevTP,
            SyncedConfig.Instance.TeleportationOddsOnShipRevTP
        );

        if (sendToTheBackrooms)
        {
            Backrooms.Instance.TeleportPlayerToBackrooms(teleportedPlayer, SyncedConfig.Instance.DropHeldItemsOnTeleport);
        }
    }

    private static bool CheckTeleportChance(PlayerControllerB player, FairRandomizerEvent eventType, float odds)
    {
        if (SyncedConfig.Instance.UseFairRandomizer)
        {
            if (!player.TryGetComponent<FairRandomizer>(out var randomizer))
            {
                randomizer = player.gameObject.AddComponent<FairRandomizer>();
            }

            return randomizer.CheckChance(eventType, odds / 100f);
        }
        else
        {
            return Random.Range(0f, 101f) < odds;
        }
    }
}