using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopJumps
{
    public class ShopJumps : BasePlugin
    {
        public override string ModuleName => "[SHOP] Jumps";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Jumps";
        public static JObject? JsonJumps { get; private set; }
        private readonly PlayerJumps[] playerJumps = new PlayerJumps[65];
        private static readonly UserSettings?[] UserSettings = new UserSettings?[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();

            RegisterListener<Listeners.OnTick>(() =>
            {
                foreach (var player in Utilities.GetPlayers()
                             .Where(player => player is { IsValid: true, IsBot: false, PawnIsAlive: true }))
                {
                    if (playerJumps[player.Slot] != null)
                    {
                        OnTick(player);
                    }
                }
            });
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Jumps.json");
            if (File.Exists(configPath))
            {
                JsonJumps = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonJumps == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Доп. прыжки");

            var sortedItems = JsonJumps
                .Properties()
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .OrderBy(p => (int)p.Value["jumps"]!)
                .ToList();

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(item.Key, (string)item.Value["name"]!, CategoryName, (int)item.Value["price"]!, (int)item.Value["sellprice"]!, (int)item.Value["duration"]!);
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
            {
                playerJumps[playerSlot] = null!;
                UserSettings[playerSlot] = null!;
            });
        }

        public void OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetJumps(uniqueName, out int Jumps))
            {
                playerJumps[player.Slot] = new PlayerJumps(Jumps, itemId);
                UserSettings[player.Slot] = new UserSettings();
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'jumps' in config!");
            }
        }

        public void OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetJumps(uniqueName, out int Jumps))
            {
                playerJumps[player.Slot] = new PlayerJumps(Jumps, itemId);
                UserSettings[player.Slot] = new UserSettings();
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
        }

        public void OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerJumps[player.Slot] = null!;
            UserSettings[player.Slot] = null!;
        }

        private static bool TryGetJumps(string uniqueName, out int Jumps)
        {
            Jumps = 0;
            if (JsonJumps != null && JsonJumps.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["jumps"] != null)
            {
                Jumps = (int)jsonItem["jumps"]!;
                return true;
            }
            return false;
        }

        private void OnTick(CCSPlayerController player)
        {
            var client = player.Index;
            var playerPawn = player.PlayerPawn?.Value;

            if (playerPawn == null)
            {
                return;
            }

            if (UserSettings[client] == null)
            {
                UserSettings[client] = new UserSettings();
            }

            if (playerJumps[player.Slot] == null)
            {
                return;
            }

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;

            if ((UserSettings[client]!.LastFlags & PlayerFlags.FL_ONGROUND) != 0 &&
                (flags & PlayerFlags.FL_ONGROUND) == 0 &&
                (UserSettings[client]!.LastButtons & PlayerButtons.Jump) == 0 && (buttons & PlayerButtons.Jump) != 0)
            {
                UserSettings[client]!.JumpsCount ++;
            }
            else if ((flags & PlayerFlags.FL_ONGROUND) != 0)
                UserSettings[client]!.JumpsCount = 0;
            else if ((UserSettings[client]!.LastButtons & PlayerButtons.Jump) == 0 &&
                     (buttons & PlayerButtons.Jump) != 0 &&
                     UserSettings[client]!.JumpsCount < playerJumps[player.Slot].Jumps)
            {
                UserSettings[client]!.JumpsCount++;
                playerPawn.AbsVelocity.Z = 300;
            }

            UserSettings[client]!.LastFlags = flags;
            UserSettings[client]!.LastButtons = buttons;
        }


        [GameEventHandler(HookMode.Pre)]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            if (@event.Userid != null)
            {
                CCSPlayerController player = @event.Userid;
                if (playerJumps[player.Slot] != null)
                {
                    if (UserSettings[player.Index] == null)
                    {
                        UserSettings[player.Index] = new UserSettings();
                    }
                    UserSettings[player.Index]!.NumberOfJumps = playerJumps[player.Slot].Jumps;
                }
            }
            return HookResult.Continue;
        }

        public record class PlayerJumps(int Jumps, int ItemID);
    }

    public class UserSettings
    {
        public PlayerButtons LastButtons { get; set; }
        public PlayerFlags LastFlags { get; set; }
        public int JumpsCount { get; set; }
        public int NumberOfJumps { get; set; }
    }
}
