//#define DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using System.Text;
using System.Linq;
using Oxide.Core.Plugins;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Recycler Cost", "RFC1920", "1.0.0")]
    [Description("Oxide Plugin")]
    class RecycleCost : RustPlugin
    {
        #region vars
        [PluginReference]
        private Plugin Economics, ServerRewards;

        const string RCGUI = "recyclecost.label";
        private const string permRecyleCostBypass = "recyclecost.bypass";
        private Dictionary<uint, ulong> rcloot = new Dictionary<uint, ulong>();

        private int costPerCycle;
        private string costItem;
        private bool useEconomics;
        private bool useServerRewards;
        private bool recycleReward;
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        void Init()
        {
            permission.RegisterPermission(permRecyleCostBypass, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["requires"] = "Requires {0} {1} per cycle",
                ["coins"] = "coins"
            }, this);
        }

        void Loaded()
        {
            costPerCycle = GetConfig("Settings", "costPerCycle", 1);
            costItem = GetConfig("Settings", "costItem", "wood");
            useEconomics = Convert.ToBoolean(GetConfig("Settings", "useEconomics", "false"));
            useServerRewards = Convert.ToBoolean(GetConfig("Settings", "useServerRewards", "false"));
            recycleReward = Convert.ToBoolean(GetConfig("Settings", "recycleReward", "false"));
        }

        void Unload()
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, RCGUI);
            }
        }

        protected override void LoadDefaultConfig()
        {
            Config["Settings", "costPerCycle"] = 1;
            Config["Settings", "costItem"] = "wood";
            Config["Settings", "useEconomics"] = false;
            Config["Settings", "useServerRewards"] = false;
            Config["Settings", "recycleReward"] = false;
            SaveConfig();
        }
        #endregion

        #region config
        private T GetConfig<T>(string name, T defaultValue)
        {
            if(Config [name] == null)
            {
                return defaultValue;
            }

            return(T)Convert.ChangeType(Config [name], typeof(T));
        }

        private T GetConfig<T>(string name, string name2, T defaultValue)
        {
            if(Config [name, name2] == null)
            {
                return defaultValue;
            }

            return(T)Convert.ChangeType(Config [name, name2], typeof(T));
        }
        #endregion

        #region Main
        object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            if(item.info.shortname != costItem) return null;
            ItemContainer originalContainer = item.GetRootContainer();
            var rc = originalContainer.entityOwner;
            if(rc == null) return null;
            if(rc.name.Contains("recycler_static"))
            {
#if DEBUG
                Puts($"Found recycler {rc.net.ID.ToString()}!");
#endif
                if(!rcloot.ContainsKey(rc.net.ID))
                {
#if DEBUG
                    Puts("Not currently managing this recycler.");
#endif
                    return null;
                }
                if((rc as Recycler).IsOn())
                {
#if DEBUG
                    Puts("Recycler is on!");
#endif
                    return false;
                }
            }
            return null;
        }

        object OnRecycleItem(Recycler recycler, Item item)
        {
            if(!rcloot.ContainsKey(recycler.net.ID)) return null;
            if(useEconomics || useServerRewards)
            {
                var player = FindPlayer(rcloot[recycler.net.ID]);
                if(recycleReward)
                {
                    CheckEconomy(player, (double) costPerCycle, false, true);
                    return null;
                }
                else if(CheckEconomy(player, (double) costPerCycle, true))
                {
                    return null;
                }
                else
                {
                    recycler.StopRecycling();
                    return true;
                }
            }
            else
            {
                CostItemCheck(recycler, null, true);
            }
            return null;
        }

        object OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if(recycler.IsOn()) return null;

            if(useEconomics || useServerRewards)
            {
                return null;
            }
            else
            {
                if(CostItemCheck(recycler, player.IPlayer)) return null;
            }

            return true;
        }

        bool CostItemCheck(Recycler recycler, IPlayer player, bool decrement = false)
        {
            if(player.HasPermission(permRecyleCostBypass)) return true;

            for(int i=0;i<6;i++)
            {
                Item item = recycler.inventory.GetSlot(i);
                if(item == null) continue;
#if DEBUG
                Puts($"{i.ToString()} Found {item.info.name}");
#endif
                if(item.info.name == costItem + ".item")
                {
                    if(item.amount < costPerCycle)
                    {
                        return false;
                    }
                    if(decrement)
                    {
#if DEBUG
                        Puts("Decrementing 1 costitem for great justice.");
#endif
                        item.amount -= costPerCycle;
                        if(item.amount <= 0)
                        {
#if DEBUG
                            Puts("No more fuel!");
#endif
                            item.RemoveFromContainer();
                            recycler.StopRecycling();
                        }
                        recycler.inventory.MarkDirty();
                    }
                    return true;
                }
            }
            recycler.StopRecycling();
            return false;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            var rc = container.GetComponentInParent<Recycler>() ?? null;
            if(rc == null) return null;

#if DEBUG
            Puts($"Adding recycler {rc.net.ID.ToString()}");
#endif
            if(rcloot.ContainsKey(rc.net.ID))
            {
                // if using eco or sw, another player can enter and the cost will shift to that player
                rcloot.Remove(rc.net.ID);
            }
            rcloot.Add(rc.net.ID, player.userID);

            rcGUI(player, rc);
            return null;
        }

        void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if(!rcloot.ContainsKey(entity.net.ID)) return;
            if(entity == null) return;

            if(rcloot[entity.net.ID] == player.userID)
            {
#if DEBUG
                Puts($"Removing recycler {entity.net.ID.ToString()}");
#endif
                CuiHelper.DestroyUi(player, RCGUI);

                // Fix end looting removes key, so economics does not cost anything...
                if(!((entity as Recycler).IsOn() && (useEconomics || useServerRewards)))
                {
                    rcloot.Remove(entity.net.ID);
                }
            }
        }

        void rcGUI(BasePlayer player, BaseEntity entity)
        {
            CuiHelper.DestroyUi(player, RCGUI);

            CuiElementContainer container = UI.Container(RCGUI, UI.Color("626262", 1f), "0.75 0.554", "0.9465 0.59", true, "Overlay");
            if(useEconomics || useServerRewards)
            {
                UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("requires", null, costPerCycle.ToString(), Lang("coins")), 18, "0 0", "1 1");
            }
            else
            {
                UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("requires", null, costPerCycle.ToString(), costItem), 18, "0 0", "1 1");
            }

            CuiHelper.AddUi(player, container);
        }

        List<BasePlayer> GetOnlinePlayers()
        {
            List<BasePlayer> available = new List<BasePlayer>();
            foreach (BasePlayer online in BasePlayer.activePlayerList) available.Add(online);
            return available;
        }

        BasePlayer FindPlayer(ulong ID)
        {
            BasePlayer result = null;
            foreach(BasePlayer current in GetOnlinePlayers())
            {
                if(current.userID == ID) result = current;
            }
            return result;
        }

        // Check balance on multiple plugins and optionally withdraw money from the player
        private bool CheckEconomy(BasePlayer player, double bypass, bool withdraw = false, bool deposit = false)
        {
            double balance = 0;
            bool foundmoney = false;

            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if(useEconomics && Economics)
            {
                balance = (double)Economics?.CallHook("Balance", player.UserIDString);
                if(balance >= bypass)
                {
                    foundmoney = true;
                    if(withdraw == true)
                    {
                        var w = (bool)Economics?.CallHook("Withdraw", player.userID, bypass);
                        return w;
                    }
                    else if(deposit == true)
                    {
                        var w = (bool)Economics?.CallHook("Deposit", player.userID, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if(useServerRewards && ServerRewards)
            {
                object bal = ServerRewards?.Call("CheckPoints", player.userID);
                balance = Convert.ToDouble(bal);
                if(balance >= bypass && foundmoney == false)
                {
                    foundmoney = true;
                    if(withdraw == true)
                    {
                        var w = (bool)ServerRewards?.Call("TakePoints", player.userID, (int)bypass);
                        return w;
                    }
                    else if(deposit == true)
                    {
                        var w = (bool)ServerRewards?.Call("AddPoints", player.userID, (int)bypass);
                    }
                }
            }

            // Just checking balance without withdrawal - did we find anything?
            return foundmoney;
        }
        #endregion

        #region Classes
        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                CuiElementContainer container = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = {AnchorMin = min, AnchorMax = max},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
                return container;
            }
            public static void Panel(ref CuiElementContainer container, string panel, string color, string min, string max, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    CursorEnabled = cursor
                },
                panel);
            }
            public static void Label(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                },
                panel);

            }
            public static void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }
            public static void Input(ref CuiElementContainer container, string panel, string color, string text, int size, string command, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            CharsLimit = 30,
                            Color = color,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max },
                        new CuiNeedsCursorComponent()
                    }
                });
            }
            public static string Color(string hexColor, float alpha)
            {
                if(hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.Substring(1);
                }
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion
    }
}
