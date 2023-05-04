#region License (GPL v2)
/*
    DESCRIPTION
    Copyright (c) 2020-2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; version 2
   of the License only

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v2)
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
    [Info("Recycler Cost", "RFC1920", "1.0.6")]
    [Description("Recycling cost via fuel or Economics/ServerRewards")]
    internal class RecycleCost : RustPlugin
    {
        #region vars
        [PluginReference]
        private readonly Plugin Economics, ServerRewards, BankSystem;

        private ConfigData configData;

        private const string RCGUI = "recyclecost.label";
        private const string permRecyleCostBypass = "recyclecost.bypass";
        private Dictionary<uint, ulong> rcloot = new Dictionary<uint, ulong>();
        #endregion

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        #region init
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["requires"] = "Requires {0} {1} per cycle",
                ["fuelslot"] = "(Fuel must be in rightmost slot)",
                ["rewards"] = "Rewards {0} coin(s) per cycle",
                ["coins"] = "coin(s)"
            }, this);
        }

        private void Init()
        {
            permission.RegisterPermission(permRecyleCostBypass, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, RCGUI);
            }
        }
        #endregion

        #region Main
        private object CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            if (item.info.name != configData.Settings.costItem) return null;
            ItemContainer originalContainer = item.GetRootContainer();
            BaseEntity rc = originalContainer.entityOwner;
            if (rc == null) return null;
            if (rc.name.Contains("recycler_static"))
            {
                if (configData.Settings.debug) Puts($"Found recycler {rc.net.ID}!");
                if (!rcloot.ContainsKey((uint)rc.net.ID.Value))
                {
                    if (configData.Settings.debug) Puts("Not currently managing this recycler.");
                    return null;
                }
                Recycler realrc = rc as Recycler;
                if (realrc?.IsOn() == true)
                {
                    if (configData.Settings.debug) Puts("Recycler is on!");
                    return false;
                }
            }
            return null;
        }

        private bool CanRecycle(Recycler recycler, Item item)
        {
            Puts("CanRecycle works!");
            if (configData.Settings.useEconomics || configData.Settings.useServerRewards || configData.Settings.useBankSystem)
            {
                Puts("Bailing out on CanRecycle");
                return true;
            }
            return item.info.name != configData.Settings.costItem;
        }

        private object OnItemRecycle(Item item, Recycler recycler)
        {
            Puts("OnItemRecycle works!");
            if (!rcloot.ContainsKey((uint)recycler.net.ID.Value)) return null;
            if (configData.Settings.useEconomics || configData.Settings.useServerRewards || configData.Settings.useBankSystem)
            {
                BasePlayer player = FindPlayerById(rcloot[(uint)recycler.net.ID.Value]);
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
                if (configData.Settings.debug) Puts($"Economics and ServerRewards disabled.  Running cost item check for {item.info.name}");
                if (!HasRecycleable(recycler)) recycler.StopRecycling();

                //if (item.info.name == costItem)
                //{
                //    return true;
                //}
                CostItemCheck(recycler, null, true);
            }
            return null;
        }

        private object OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if (recycler.IsOn()) return null;

            if (configData.Settings.useEconomics || configData.Settings.useServerRewards || configData.Settings.useBankSystem)
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

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            Recycler rc = container?.GetComponentInParent<Recycler>();
            if (rc == null) return null;

            if (configData.Settings.debug) Puts($"Adding recycler {rc.net.ID}");
            if (rcloot.ContainsKey((uint)rc.net.ID.Value))
            {
                // if using eco or sw, another player can enter and the cost will shift to that player
                rcloot.Remove((uint)rc.net.ID.Value);
            }
            rcloot.Add((uint)rc.net.ID.Value, player.userID);

            rcGUI(player);
            return null;
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (entity == null) return;
            if (!rcloot.ContainsKey((uint)entity.net.ID.Value)) return;

            if (rcloot[(uint)entity.net.ID.Value] == player.userID)
            {
                if (configData.Settings.debug) Puts($"Removing recycler {entity.net.ID}");
                CuiHelper.DestroyUi(player, RCGUI);

                Recycler realrc = entity as Recycler;
                if (realrc?.IsOn() == false && (configData.Settings.useEconomics || configData.Settings.useServerRewards || configData.Settings.useBankSystem))
                {
                    rcloot.Remove((uint)entity.net.ID.Value);
                }
            }
        }


        // Verify that something other than our costItem is present
        private bool HasRecycleable(Recycler recycler)
        {
            if (configData.Settings.debug) Puts($"Checking for recycleables other than {configData.Settings.costItem}.");
            bool found = false;
            bool foundItem = false;
            bool empty = true;

            for (int i = 0; i < 6; i++)
            {
                try
                {
                    Item item = recycler.inventory.GetSlot(i);
                    if (configData.Settings.debug) Puts($"Found {item.info.name} in slot {i}");
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
                if (configData.Settings.debug) Puts("Recycler input is empty...");
                return false;
            }
            else if (found && foundItem)
            {
                if (configData.Settings.debug) Puts($"Found recycleables and {configData.Settings.costItem} in this recycler!");
            }
            else if (found)
            {
                if (configData.Settings.debug) Puts($"Did not find anything other than our costItem, {configData.Settings.costItem}, in this recycler!");
            }

            return foundItem;
        }

        private bool CostItemCheck(Recycler recycler, IPlayer player, bool decrement = false)
        {
            if (player?.HasPermission(permRecyleCostBypass) == true)
            {
                return true;
            }

            for (int i = 0; i < 6; i++)
            {
                Item item = recycler.inventory.GetSlot(i);
                if (item == null) continue;
                if (configData.Settings.debug) Puts($"{i} Found {item.info.name}");
                if (item.info.name == configData.Settings.costItem)
                {
                    if (item.amount < configData.Settings.costPerCycle)
                    {
                        return false;
                    }
                    if (decrement)
                    {
                        if (configData.Settings.debug) Puts("Decrementing 1 costitem for great justice.");
                        item.amount -= configData.Settings.costPerCycle;
                        if (item.amount <= 0)
                        {
                            if (configData.Settings.debug) Puts("No more fuel!");
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

        private void rcGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, RCGUI);
            CuiElementContainer container;

            if (configData.Settings.useEconomics || configData.Settings.useServerRewards || configData.Settings.useBankSystem)
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

        private static BasePlayer FindPlayerById(ulong userid)
        {
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.userID == userid)
                {
                    return current;
                }
            }
            return null;
        }

        // Check balance on multiple plugins and optionally withdraw money from the player
        private bool CheckEconomy(BasePlayer player, double bypass, bool withdraw = false, bool deposit = false)
        {
            bool foundmoney = false;
            double balance;

            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if (configData.Settings.useEconomics && Economics)
            {
                balance = (double)Economics?.CallHook("Balance", player.UserIDString);
                if (balance >= bypass)
                {
                    foundmoney = true;
                    if (withdraw)
                    {
                        return (bool)Economics?.CallHook("Withdraw", player.userID, bypass);
                    }
                    else if (deposit)
                    {
                        Economics?.CallHook("Deposit", player.userID, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if (configData.Settings.useServerRewards && ServerRewards)
            {
                object bal = ServerRewards?.Call("CheckPoints", player.userID);
                balance = Convert.ToDouble(bal);
                if (balance >= bypass && !foundmoney)
                {
                    foundmoney = true;
                    if (withdraw)
                    {
                        return (bool)ServerRewards?.Call("TakePoints", player.userID, (int)bypass);
                    }
                    else if (deposit)
                    {
                        ServerRewards?.Call("AddPoints", player.userID, (int)bypass);
                    }
                }
            }

            // No money via Economics nor ServerRewards, or plugins not in use.  Try BankSystem.
            if (configData.Settings.useBankSystem && BankSystem)
            {
                object bal = BankSystem?.Call("Balance", player.userID);
                balance = Convert.ToDouble(bal);
                if (balance >= bypass && !foundmoney)
                {
                    foundmoney = true;
                    if (withdraw)
                    {
                        return (bool)BankSystem?.Call("Withdraw", player.userID, (int)bypass);
                    }
                    else if (deposit)
                    {
                        bool w = (bool)BankSystem?.Call("Deposit", player.userID, (int)bypass);
                    }
                }
            }
            // Just checking balance without withdrawal - did we find anything?
            return foundmoney;
        }
        #endregion

        #region config
        private class ConfigData
        {
            public Settings Settings;
            public VersionNumber Version;
        }

        private class Settings
        {
            public int costPerCycle;
            public string costItem;
            public bool useEconomics;
            public bool useServerRewards;
            public bool useBankSystem;
            public bool recycleReward;
            public bool debug;
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
                    costItem = "fuel.lowgrade.item",
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

        #region Classes
        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer()
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
