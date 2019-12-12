﻿using System.Collections.Generic;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
using WeaponCore.Support;

namespace WeaponCore
{
    internal partial class Wheel
    {
        internal struct GroupMember
        {
            internal string Name;
            internal WeaponComponent Comp;
        }

        internal class Item
        {
            internal string Title;
            internal string ItemMessage;
            internal string SubName;
            internal string ParentName;
            internal int SubSlot;
            internal int SubSlotCount;
            internal bool Dynamic;
        }

        internal class Menu
        {
            internal enum Movement
            {
                Forward,
                Backward,
            }

            internal readonly Wheel Wheel;
            internal readonly string Name;
            internal readonly Item[] Items;
            internal readonly int ItemCount;
            internal int CurrentSlot;
            internal List<string> GroupNames;
            internal List<List<GroupMember>> BlockGroups;
            internal MyEntity GpsEntity;

            private string _message;
            public string Message
            {
                get { return _message ?? string.Empty; }
                set { _message = value ?? string.Empty; }
            }

            internal Menu(Wheel wheel, string name, Item[] items, int itemCount)
            {
                Wheel = wheel;
                Name = name;
                Items = items;
                ItemCount = itemCount;
            }

            internal string CurrentItemMessage()
            {
                return Items[CurrentSlot].ItemMessage;
            }

            internal void Move(Movement move)
            {
                switch (move)
                {
                    case Movement.Forward:
                        {
                            if (ItemCount > 1)
                            {
                                if (CurrentSlot < ItemCount - 1) CurrentSlot++;
                                else CurrentSlot = 0;
                                Message = Items[CurrentSlot].ItemMessage;
                            }
                            else
                            {
                                var item = Items[0];
                                if (item.SubSlot < item.SubSlotCount - 1) item.SubSlot++;
                                else item.SubSlot = 0;
                                GetInfo(item);
                            }

                            break;
                        }
                    case Movement.Backward:
                        if (ItemCount > 1)
                        {
                            if (CurrentSlot - 1 >= 0) CurrentSlot--;
                            else CurrentSlot = ItemCount - 1;
                            Message = Items[CurrentSlot].ItemMessage;
                        }
                        else
                        {
                            var item = Items[0];
                            if (item.SubSlot - 1 >= 0) item.SubSlot--;
                            else item.SubSlot = item.SubSlotCount - 1;
                            GetInfo(item);
                        }
                        break;
                }
            }

            internal void GetInfo(Item item)
            {
                GroupInfo groupInfo;
                switch (Name)
                {
                    case "WeaponGroups":
                        if (GroupNames.Count > 0)
                        {
                            var groupName = GroupNames[item.SubSlot];

                            if (!Wheel.Ai.BlockGroups.TryGetValue(groupName, out groupInfo)) break;
                            Wheel.ActiveGroupName = groupName;
                            FormatGroupMessage(groupInfo);
                        }
                        break;
                    case "GroupSettings":
                        if (!Wheel.Ai.BlockGroups.TryGetValue(Wheel.ActiveGroupName, out groupInfo)) break;
                        FormatGroupSettingsMessage(groupInfo);
                        break;
                    case "Comps":
                        if (BlockGroups.Count > 0)
                        {
                            var groupMember = BlockGroups[Wheel.ActiveGroupId][item.SubSlot];
                            if (!Wheel.Ai.BlockGroups.TryGetValue(groupMember.Name, out groupInfo)) break;
                            FormatWeaponMessage(groupMember, Color.Yellow);
                        }
                        break;
                    case "CompSettings":
                        if (Wheel.BlockGroups.Count > 0)
                        {
                            var groupMember = Wheel.BlockGroups[Wheel.ActiveGroupId][Wheel.ActiveWeaponId];
                            if (!Wheel.Ai.BlockGroups.TryGetValue(groupMember.Name, out groupInfo)) break;
                            FormatWeaponMessage(groupMember, Color.Green);
                            FormatMemberSettingsMessage(groupInfo, groupMember);
                        }
                        break;
                }
            }

            internal void SetInfo(Item item)
            {
                GroupInfo groupInfo;
                switch (Name)
                {
                    case "GroupSettings":
                        if (!Wheel.Ai.BlockGroups.TryGetValue(Wheel.ActiveGroupName, out groupInfo)) break;
                        SetGroupSettings(groupInfo);
                        break;
                }
                switch (Name)
                {
                    case "CompSettings":
                        if (Wheel.BlockGroups.Count > 0)
                        {
                            var groupMember = Wheel.BlockGroups[Wheel.ActiveGroupId][Wheel.ActiveWeaponId];
                            if (!Wheel.Ai.BlockGroups.TryGetValue(groupMember.Name, out groupInfo)) break;
                            SetMemberSettings(groupInfo, groupMember);
                        }
                        break;
                }
            }

            internal void SetGroupSettings(GroupInfo groupInfo)
            {
                var currentSettingName = Wheel.SettingNames[Items[CurrentSlot].SubSlot];
                var currentValue = groupInfo.Settings[currentSettingName];
                var map = Wheel.SettingCycleStrMap[currentSettingName];
                var nextValueStr = map[currentValue].NextValue;
                var nextValue = Wheel.SettingStrToValues[currentSettingName][nextValueStr];
                groupInfo.Settings[currentSettingName] = nextValue;
                groupInfo.ApplySettings();
                FormatGroupSettingsMessage(groupInfo);
            }

            internal void SetMemberSettings(GroupInfo groupInfo, GroupMember groupMember)
            {
                FormatMemberSettingsMessage(groupInfo, groupMember);
            }

            internal void FormatWeaponMessage(GroupMember groupMember, Color color)
            {
                var message = groupMember.Name;
                GpsEntity = groupMember.Comp.MyCube;
                var gpsName = GpsEntity.DisplayNameText;
                Wheel.Session.SetGpsInfo(GpsEntity.PositionComp.GetPosition(), gpsName, 0, color);
                Message = $"[{message}]";
            }

            internal void FormatGroupMessage(GroupInfo groupInfo)
            {
                var enabledValueString = Wheel.SettingCycleStrMap["Active"][groupInfo.Settings["Active"]].Value;
                var message = $"[Weapon Group:\n{groupInfo.Name} ({enabledValueString})]";
                Message = message;
            }

            internal void FormatGroupSettingsMessage(GroupInfo groupInfo)
            {
                var settingName = Wheel.SettingNames[Items[CurrentSlot].SubSlot];
                var setting = Wheel.SettingCycleStrMap[settingName];
                var currentState = setting[groupInfo.Settings[settingName]].Value;
                var message = $"[{settingName} ({currentState})]";
                Message = message;
            }

            internal void FormatMemberSettingsMessage(GroupInfo groupInfo, GroupMember groupMember)
            {
                var settingName = Wheel.SettingNames[Items[CurrentSlot].SubSlot];
                var setting = Wheel.SettingCycleStrMap[settingName];
                var currentState = setting[groupInfo.GetCompSetting(settingName, groupMember.Comp)];
                var message = $"[{settingName} ({currentState.Value})]";
                Message = message;
            }

            internal void LoadInfo()
            {
                var item = Items[0];
                item.SubSlot = 0;
                switch (Name)
                {
                    case "WeaponGroups":
                        GroupNames = Wheel.GroupNames;
                        item.SubSlotCount = GroupNames.Count;
                        break;
                    case "GroupSettings":
                        item.SubSlotCount = Wheel.SettingStrToValues.Count;
                        break;
                    case "Comps":
                        BlockGroups = Wheel.BlockGroups;
                        item.SubSlotCount = BlockGroups[Wheel.ActiveGroupId].Count;
                        break;
                    case "CompSettings":
                        item.SubSlotCount = Wheel.SettingStrToValues.Count;
                        break;
                }

                GetInfo(item);
            }

            internal void CleanUp()
            {
                GroupNames?.Clear();
                BlockGroups?.Clear();
            }
        }
    }
}
