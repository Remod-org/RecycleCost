#region License (GPL v3)
/*
    DESCRIPTION
    Copyright (c) 2020 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v3)
#define DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core.Plugins;
using System.Globalization;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Recycler Cost", "RFC1920", "1.0.3")]
    [Description("Recycling cost via fuel or Economics/ServerRewards")]
    class RecycleCost : RustPlugin
    {
        #region vars
        [PluginReference]
        private readonly Plugin Economics, ServerRewards;
        private ConfigData configData;

        const string RCGUI = "recyclecost.label";
        private const string permRecyleCostBypass = "recyclecost.bypass";
        private Dictionary<uint, ulong> rcloot = new Dictionary<uint, ulong>();
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
                ["fuelslot"] = "(Fuel must be in rightmost slot)",
                ["rewards"] = "Rewards {0} coin(s) per cycle",
                ["coins"] = "coin(s)"
            }, this);
        }

        void Loaded()
        {
            LoadConfigVariables();
        }

        void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, RCGUI);
            }
        }
        #endregion

        #region config
        private class ConfigData
        {
            public Settings Settings;
            public VersionNumber Version;
        }
        class Settings
        {
            public int costPerCycle;
            public string costItem;
            public bool useEconomics;
            public bool useServerRewards;
            public bool recycleReward;
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData
            {
                Version = Version,
                Settings = new Settings()
                {
                    costPerCycle = 1,
                    costItem = "wood.item",
                    useEconomics = false,
                    useServerRewards = false,
                    recycleReward = false
                }
            };
            SaveConfig();
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion

        #region Main
        object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            if (item.info.name != configData.Settings.costItem) return null;
            ItemContainer originalContainer = item.GetRootContainer();
            BaseEntity rc = originalContainer.entityOwner;
            if (rc == null) return null;
            if (rc.name.Contains("recycler_static"))
            {
#if DEBUG
                Puts($"Found recycler {rc.net.ID.ToString()}!");
#endif
                if (!rcloot.ContainsKey(rc.net.ID))
                {
#if DEBUG
                    Puts("Not currently managing this recycler.");
#endif
                    return null;
                }
                if ((rc as Recycler).IsOn())
                {
#if DEBUG
                    Puts("Recycler is on!");
#endif
                    return false;
                }
            }
            return null;
        }

        bool CanRecycle(Recycler recycler, Item item)
        {
            if (configData.Settings.useEconomics || configData.Settings.useServerRewards)
            {
                return true;
            }
            if (item.info.name == configData.Settings.costItem) return false;
            return true;
        }

        object OnRecycleItem(Recycler recycler, Item item)
        {
            if (!rcloot.ContainsKey(recycler.net.ID)) return null;
            if (configData.Settings.useEconomics || configData.Settings.useServerRewards)
            {
                BasePlayer player = FindPlayer(rcloot[recycler.net.ID]);
                if (player.IPlayer.HasPermission(permRecyleCostBypass)) return null;

                if (configData.Settings.recycleReward)
                {
                    CheckEconomy(player, configData.Settings.costPerCycle, false, true);
                    return null;
                }
                else if (CheckEconomy(player, configData.Settings.costPerCycle, true))
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
#if DEBUG
                Puts($"Economics and ServerRewards disabled.  Running cost item check for {item.info.name}");
#endif
                if (!HasRecycleable(recycler)) recycler.StopRecycling();

                //if (item.info.name == costItem)
                //{
                //    return true;
                //}
                CostItemCheck(recycler, null, true);
            }
            return null;
        }

        object OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if (recycler.IsOn()) return null;

            if (configData.Settings.useEconomics || configData.Settings.useServerRewards)
            {
                return null;
            }
            else
            {
                if (!HasRecycleable(recycler))
                {
                    recycler.StopRecycling();
                    return true;
                }
                if (CostItemCheck(recycler, player.IPlayer)) return null;
            }

            return true;
        }

        // Verify that something other than our costItem is present
        bool HasRecycleable(Recycler recycler)
        {
#if DEBUG
            Puts($"Checking for recycleables other than {configData.Settings.costItem}.");
#endif
            bool found = false;
            bool foundItem = false;
            bool empty = true;

            for (int i = 0; i < 6; i++)
            {
                try
                {
                    Item item = recycler.inventory.GetSlot(i);
#if DEBUG
                    Puts($"Found {item.info.name} in slot {i.ToString()}");
#endif
                    if (item.info.name != configData.Settings.costItem)
                    {
                        found = true;
                    }
                    else
                    {
                        foundItem = true;
                    }
                    empty = false;
                }
                catch {}
            }
            if (empty)
            {
#if DEBUG
                Puts("Recycler input is empty...");
#endif
                return false;
            }
            else if (found && foundItem)
            {
#if DEBUG
                Puts($"Found recycleables and {configData.Settings.costItem} in this recycler!");
#endif
            }
            else if (found)
            {
#if DEBUG
                Puts($"Did not find anything other than our costItem, {configData.Settings.costItem}, in this recycler!");
#endif
            }

            return foundItem;
        }

        bool CostItemCheck(Recycler recycler, IPlayer player, bool decrement = false)
        {
            if (player != null)
            {
                if (player.HasPermission(permRecyleCostBypass)) return true;
            }

            for (int i = 0; i < 6; i++)
            {
                Item item = recycler.inventory.GetSlot(i);
                if (item == null) continue;
#if DEBUG
                Puts($"{i.ToString()} Found {item.info.name}");
#endif
                if (item.info.name == configData.Settings.costItem)
                {
                    if (item.amount < configData.Settings.costPerCycle)
                    {
                        return false;
                    }
                    if (decrement)
                    {
#if DEBUG
                        Puts("Decrementing 1 costitem for great justice.");
#endif
                        item.amount -= configData.Settings.costPerCycle;
                        if (item.amount <= 0)
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
            Recycler rc = container.GetComponentInParent<Recycler>() ?? null;
            if (rc == null) return null;

#if DEBUG
            Puts($"Adding recycler {rc.net.ID.ToString()}");
#endif
            if (rcloot.ContainsKey(rc.net.ID))
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
            if (!rcloot.ContainsKey(entity.net.ID)) return;
            if (entity == null) return;

            if (rcloot[entity.net.ID] == player.userID)
            {
#if DEBUG
                Puts($"Removing recycler {entity.net.ID.ToString()}");
#endif
                CuiHelper.DestroyUi(player, RCGUI);

                if (!((entity as Recycler).IsOn() && (configData.Settings.useEconomics || configData.Settings.useServerRewards)))
                {
                    rcloot.Remove(entity.net.ID);
                }
            }
        }

        void rcGUI(BasePlayer player, BaseEntity entity)
        {
            CuiHelper.DestroyUi(player, RCGUI);
            CuiElementContainer container;

            if (configData.Settings.useEconomics || configData.Settings.useServerRewards)
            {
                container = UI.Container(RCGUI, UI.Color("505048", 1f), "0.725 0.554", "0.9465 0.59", true, "Overlay");
                if (configData.Settings.recycleReward)
                {
                    UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("rewards", null, configData.Settings.costPerCycle.ToString()), 16, "0 0", "1 1");
                }
                else
                {
                    UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("requires", null, configData.Settings.costPerCycle.ToString(), Lang("coins")), 16, "0 0", "1 1");
                }
            }
            else
            {
                container = UI.Container(RCGUI, UI.Color("505048", 1f), "0.75 0.554", "0.9465 0.615", true, "Overlay");

                string itemname = configData.Settings.costItem.Replace(".item", "");
                UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("fuelslot", null, configData.Settings.costPerCycle.ToString(), itemname), 14, "0 0", "0.98 0.49", TextAnchor.LowerRight);
                UI.Label(ref container, RCGUI, UI.Color("#cccccc", 1f), Lang("requires", null, configData.Settings.costPerCycle.ToString(), itemname), 18, "0 0.52", "1 1");
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
            foreach (BasePlayer current in GetOnlinePlayers())
            {
                if (current.userID == ID) result = current;
            }
            return result;
        }

        // Check balance on multiple plugins and optionally withdraw money from the player
        private bool CheckEconomy(BasePlayer player, double bypass, bool withdraw = false, bool deposit = false)
        {
            double balance = 0;
            bool foundmoney = false;

            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if (configData.Settings.useEconomics && Economics)
            {
                balance = (double)Economics?.CallHook("Balance", player.UserIDString);
                if (balance >= bypass)
                {
                    foundmoney = true;
                    if (withdraw == true)
                    {
                        bool w = (bool)Economics?.CallHook("Withdraw", player.userID, bypass);
                        return w;
                    }
                    else if (deposit == true)
                    {
                        bool w = (bool)Economics?.CallHook("Deposit", player.userID, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if (configData.Settings.useServerRewards && ServerRewards)
            {
                object bal = ServerRewards?.Call("CheckPoints", player.userID);
                balance = Convert.ToDouble(bal);
                if (balance >= bypass && foundmoney == false)
                {
                    foundmoney = true;
                    if (withdraw == true)
                    {
                        bool w = (bool)ServerRewards?.Call("TakePoints", player.userID, (int)bypass);
                        return w;
                    }
                    else if (deposit == true)
                    {
                        bool w = (bool)ServerRewards?.Call("AddPoints", player.userID, (int)bypass);
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
                if (hexColor.StartsWith("#"))
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
