﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private bool ClickFirstFriendJoinButton()
        {
            GameObject panel = GameObject.Find(LOGIN_ROOM_PANEL_PATH);
            if (panel == null || !panel.activeInHierarchy)
            {
                return false;
            }

            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0)
            {
                return false;
            }

            foreach (Button btn in buttons)
            {
                if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                {
                    continue;
                }

                string path = this.GetHierarchyPath(btn.transform);
                if (string.IsNullOrEmpty(path) || !IsLobbyFriendJoinButton(btn.transform, path))
                {
                    continue;
                }

                btn.onClick.Invoke();
                return true;
            }

            return false;
        }

        // RoomCellWidget, 2026-09-24+: every non-self cell joins through nodes/join@btn, and a friend
        // cell is the one whose state@frame/friend@go is shown (the game hides join@btn while the
        // friend is offline, so the activeInHierarchy check already skips those). Older builds had a
        // dedicated friend@go/friend@btn.
        private static bool IsLobbyFriendJoinButton(Transform btn, string path)
        {
            if (path.Contains("/friend@go/friend@btn"))
            {
                return true;
            }

            if (!path.EndsWith("/nodes/join@btn", StringComparison.Ordinal))
            {
                return false;
            }

            Transform cell = btn.parent != null ? btn.parent.parent : null;
            Transform friendGo = cell != null ? cell.Find("state@frame/friend@go") : null;
            return friendGo != null && friendGo.gameObject.activeInHierarchy;
        }

        private void StartLobbyAutoJoinFriend(string reason)
        {
            this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.OpenRoomPanel;
            this.lobbyJoinInProgress = true;
            this.lobbyJoinIsMyTown = false;
            this.lobbyJoinRefreshAttempts = 0;
            this.lobbyJoinNextActionAt = Time.unscaledTime;
            this.lobbyAutoJoinStatus = "Starting auto join...";
            this.lobbyNextAutoJoinAttemptAt = Time.unscaledTime + 2f;
            this.AddMenuNotification($"Auto Join Friend: {reason}", new Color(0.55f, 0.88f, 1f));
        }

        private void StopLobbyAutoJoin(string status)
        {
            this.lobbyJoinInProgress = false;
            this.lobbyJoinIsMyTown = false;
            this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.Idle;
            this.lobbyAutoJoinStatus = status;
            this.lobbyJoinNextActionAt = 0f;
            this.lobbyJoinRefreshAttempts = 0;
            this.lobbyNextAutoJoinAttemptAt = Time.unscaledTime + 2f;
        }

        private void StartLobbyAutoJoinMyTown(string reason)
        {
            this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.OpenRoomPanel;
            this.lobbyJoinInProgress = true;
            this.lobbyJoinIsMyTown = true;
            this.lobbyJoinRefreshAttempts = 0;
            this.lobbyJoinNextActionAt = Time.unscaledTime;
            this.lobbyAutoJoinStatus = "Starting auto join My Town...";
            this.lobbyNextAutoJoinAttemptAt = Time.unscaledTime + 2f;
            this.AddMenuNotification($"Auto Join My Town: {reason}", new Color(0.55f, 0.88f, 1f));
        }

        private void RunLobbyAutoActions()
        {
            if (!this.autoJoinFriendEnabled && !this.autoClickStartEnabled && !this.lobbyJoinInProgress)
            {
                return;
            }

            if (!this.IsLoginPanelActive())
            {
                if (this.lobbyJoinInProgress)
                {
                    this.StopLobbyAutoJoin("Stopped (left lobby)");
                }
                return;
            }

            if (this.autoJoinFriendEnabled && !this.lobbyJoinInProgress && Time.unscaledTime >= this.lobbyNextAutoJoinAttemptAt)
            {
                this.StartLobbyAutoJoinFriend("Auto mode");
            }

            if (this.autoClickStartEnabled && !this.autoJoinFriendEnabled && !this.lobbyJoinInProgress && Time.unscaledTime >= this.lobbyNextAutoStartClickAt)
            {
                if (!this.IsLoginRoomPanelActive() && this.ClickButtonIfExistsReturn(START_GAME_BUTTON_PATH))
                {
                    this.lobbyAutoJoinStatus = "Clicked Start";
                }
                this.lobbyNextAutoStartClickAt = Time.unscaledTime + 2f;
            }

            if (!this.lobbyJoinInProgress || Time.unscaledTime < this.lobbyJoinNextActionAt)
            {
                return;
            }

            switch (this.lobbyJoinState)
            {
                case HeartopiaComplete.LobbyJoinState.OpenRoomPanel:
                    if (this.IsLoginRoomPanelActive())
                    {
                        if (this.lobbyJoinIsMyTown)
                        {
                            this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.SelectMyTownTab;
                        }
                        else
                        {
                            this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.SelectFriendTab;
                        }
                        this.lobbyJoinNextActionAt = Time.unscaledTime + 0.3f;
                        this.lobbyAutoJoinStatus = "Room panel opened";
                        break;
                    }
                    if (this.ClickButtonIfExistsReturn(ROOM_ENTRY_BUTTON_PATH))
                    {
                        this.lobbyAutoJoinStatus = "Opening room panel...";
                        this.lobbyJoinNextActionAt = Time.unscaledTime + 0.6f;
                    }
                    else
                    {
                        this.StopLobbyAutoJoin("Room button not found");
                    }
                    break;

                case HeartopiaComplete.LobbyJoinState.SelectFriendTab:
                    this.ClickButtonIfExistsReturn(FRIEND_TAB_BUTTON_PATH);
                    this.lobbyAutoJoinStatus = "Selecting Friend's Town tab...";
                    this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.ClickFriendJoin;
                    this.lobbyJoinNextActionAt = Time.unscaledTime + 0.4f;
                    break;

                case HeartopiaComplete.LobbyJoinState.ClickFriendJoin:
                    if (this.ClickFirstFriendJoinButton())
                    {
                        this.StopLobbyAutoJoin("Joined friend town");
                        break;
                    }

                    if (this.lobbyJoinRefreshAttempts < 2)
                    {
                        this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.RefreshAndRetry;
                        this.lobbyJoinNextActionAt = Time.unscaledTime + 0.2f;
                        this.lobbyAutoJoinStatus = "No friend slot, refreshing...";
                    }
                    else
                    {
                        this.StopLobbyAutoJoin("No friend town found");
                    }
                    break;

                case HeartopiaComplete.LobbyJoinState.SelectMyTownTab:
                    this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)/AniRoot/popup/content/background/tab_bg/tabBar@w/tab@list/Viewport/Content/self@w/cell@btn");
                    this.lobbyAutoJoinStatus = "Selecting My Town tab...";
                    this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.ClickMyTownJoin;
                    this.lobbyJoinNextActionAt = Time.unscaledTime + 0.4f;
                    break;

                case HeartopiaComplete.LobbyJoinState.ClickMyTownJoin:
                    // nodes/selfRoomEnter@btn since 2026-09-24; selfRoom@go/selfRoomEnter@btn before.
                    if (this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)/AniRoot/popup/content/background/town@unbreakscroll/Content/RoomCellWidget/nodes/selfRoomEnter@btn")
                        || this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)/AniRoot/popup/content/background/town@unbreakscroll/Content/RoomCellWidget/selfRoom@go/selfRoomEnter@btn"))
                    {
                        this.StopLobbyAutoJoin("Joined My Town");
                        break;
                    }
                    else
                    {
                        this.StopLobbyAutoJoin("My Town join button not found");
                    }
                    break;

                case HeartopiaComplete.LobbyJoinState.RefreshAndRetry:
                    this.ClickButtonIfExistsReturn(ROOM_REFRESH_BUTTON_PATH);
                    this.lobbyJoinRefreshAttempts++;
                    this.lobbyJoinState = HeartopiaComplete.LobbyJoinState.SelectFriendTab;
                    this.lobbyJoinNextActionAt = Time.unscaledTime + 0.7f;
                    this.lobbyAutoJoinStatus = $"Refresh {this.lobbyJoinRefreshAttempts}/2";
                    break;
            }
        }

        private enum LobbyJoinState
        {
            Idle,
            OpenRoomPanel,
            SelectFriendTab,
            RefreshAndRetry,
            ClickFriendJoin,
            SelectMyTownTab,
            ClickMyTownJoin
        }

    }
}
