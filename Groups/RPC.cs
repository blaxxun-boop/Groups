﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Groups;

public static class RPC
{
	private static long pendingInvitationSenderId;
	private static readonly Dictionary<string, PlayerReference> characterIdCache = new();

	[HarmonyPatch(typeof(Game), nameof(Game.Start))]
	private class AddRPCs
	{
		private static void Postfix()
		{
			ZRoutedRpc.instance.Register<string>("Groups AddMessage", (_, message) =>
			{
				Chat.instance.AddString(message);
				Chat.instance.m_hideTimer = 0f;
			});
			ZRoutedRpc.instance.Register<UserInfo, string>("Groups ChatMessage", onChatMessageReceived);
			ZRoutedRpc.instance.Register<string>("Groups InvitePlayer", onInvitationReceived);
			ZRoutedRpc.instance.Register("Groups ForcedInvitation", onForcedInvitationReceived);
			ZRoutedRpc.instance.Register<string>("Groups CombatNotification", onCombatNotificationReceived);
			ZRoutedRpc.instance.Register<ZPackage>("Groups AcceptInvitation", onInvitationAccepted);
			ZRoutedRpc.instance.Register<ZPackage>("Groups AcceptInvitationResponse", onInvitationAcceptedResponse);
			ZRoutedRpc.instance.Register<string, string>("Groups UpdateGroup", onUpdateGroup);
			ZRoutedRpc.instance.Register<string, ZPackage>("Groups AddMember", onNewGroupMember);
			ZRoutedRpc.instance.Register<float, float>("Groups UpdateHealth", Interface.onUpdateHealth);
			ZRoutedRpc.instance.Register<Vector3>("Groups UpdatePosition", onUpdatePosition);
			ZRoutedRpc.instance.Register<Vector3, int, UserInfo, string>("Groups MapPing", Map.onMapPing);
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
	private class SetCharacterId
	{
		private static void Postfix(Player __instance)
		{
			ZNet.instance.m_characterID = __instance.GetZDOID();
		}
	}

	[HarmonyPatch]
	private class RemoveFromGroupOnLogoutAndPreservePosition
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.RPC_PlayerList)),
			AccessTools.DeclaredMethod(typeof(ZNet), nameof(ZNet.UpdatePlayerList)),
		};

		private static void Prefix(ZNet __instance, out Dictionary<long, Vector3> __state)
		{
			__state = new Dictionary<long, Vector3>();

			if (Groups.ownGroup is null)
			{
				return;
			}

			foreach (ZNet.PlayerInfo playerInfo in __instance.m_players.Where(p => Groups.ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayerId(p.m_characterID.UserID))))
			{
				__state[playerInfo.m_characterID.UserID] = playerInfo.m_position;
			}
		}

		private static void Postfix(ZNet __instance, Dictionary<long, Vector3> __state)
		{
			foreach (ZNet.PlayerInfo player in __instance.m_players)
			{
				if (player.m_characterID != ZDOID.None)
				{
					characterIdCache[player.m_host] = PlayerReference.fromPlayerInfo(player);
				}
			}
			foreach (string key in characterIdCache.Keys.Where(host => __instance.m_players.All(p => p.m_host != host)).ToArray())
			{
				characterIdCache.Remove(key);
			}

			ChatCommands.UpdateAutoCompletion();

			if (Groups.ownGroup is null)
			{
				return;
			}

			List<ZNet.PlayerInfo> playerInfos = new();
			foreach (ZNet.PlayerInfo playerInfo in __instance.m_players)
			{
				ZNet.PlayerInfo info = playerInfo;
				characterIdCache.TryGetValue(info.m_host, out PlayerReference player);
				if (Groups.ownGroup.playerStates.ContainsKey(player) && player.peerId != ZDOMan.GetSessionID())
				{
					if (__state.TryGetValue(playerInfo.m_characterID.UserID, out Vector3 position))
					{
						if (!playerInfo.m_publicPosition)
						{
							info.m_position = position;
						}
					}
					info.m_publicPosition = true;
				}
				playerInfos.Add(info);
			}
			__instance.m_players = playerInfos;

			if (Groups.ownGroup.leader.peerId != ZDOMan.GetSessionID())
			{
				bool leaderIsOnline = characterIdCache.ContainsValue(Groups.ownGroup.leader);
				if (leaderIsOnline || Groups.ownGroup.playerStates.Keys.OrderBy(p => p.peerId).First(characterIdCache.Values.Contains).peerId != ZDOMan.GetSessionID())
				{
					return;
				}

				Groups.ownGroup.PromoteMember(PlayerReference.fromPlayerId(ZDOMan.GetSessionID()));
			}

			foreach (PlayerReference player in Groups.ownGroup.playerStates.Keys.Except(characterIdCache.Values).ToArray())
			{
				Groups.ownGroup.RemoveMember(player, true);
			}
		}
	}

	private static void onUpdateGroup(long senderId, string playerReference, string action)
	{
		if (Groups.ownGroup is null)
		{
			return;
		}

		switch (action)
		{
			case "Member Removed":
			{
				PlayerReference reference = PlayerReference.fromString(playerReference);
				if (reference == PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					API.InvokeGroupLeft();

					ChatCommands.ToggleGroupsChat(false);
					Groups.ownGroup = null;
				}
				else
				{
					Groups.ownGroup.playerStates.Remove(reference);
					API.InvokeMemberLeft(reference);
				}

				ChatCommands.UpdateAutoCompletion();

				break;
			}
			case "Member Promoted":
			{
				Groups.ownGroup.leader = PlayerReference.fromString(playerReference);

				ChatCommands.UpdateAutoCompletion();

				break;
			}
		}
	}

	private static void onNewGroupMember(long senderId, string playerReference, ZPackage playerState)
	{
		if (Groups.ownGroup is null)
		{
			return;
		}

		PlayerReference reference = PlayerReference.fromString(playerReference);

		Groups.ownGroup.playerStates.Add(reference, Group.PlayerState.read(playerState));
		API.InvokeMemberJoined(reference);

		ChatCommands.UpdateAutoCompletion();
	}

	private static void onChatMessageReceived(long senderId, UserInfo name, string message)
	{
		Chat.instance.AddString("<color=orange>" + name.Name + "</color>: <color=#" + ColorUtility.ToHtmlStringRGBA(Groups.groupChatColor.Value) + ">" + message + "</color>");
		Chat.instance.m_hideTimer = 0f;
		ZDOID playerZDO = ZNet.instance.m_players.FirstOrDefault(p => p.m_characterID.UserID == senderId).m_characterID;
		if (playerZDO != ZDOID.None && ZNetScene.instance.FindInstance(playerZDO) is { } playerObject && playerObject.GetComponent<Player>() is { } player)
		{
			if (Minimap.instance && Player.m_localPlayer && Minimap.instance.m_mode == Minimap.MapMode.None && Vector3.Distance(Player.m_localPlayer.transform.position, player.GetHeadPoint()) > Minimap.instance.m_nomapPingDistance)
			{
				return;
			}
			Chat.instance.AddInworldText(playerObject, senderId, player.GetHeadPoint(), Talker.Type.Normal, name, "<color=#" + ColorUtility.ToHtmlStringRGBA(Groups.groupChatColor.Value) + ">" + message + "</color>");
		}
	}

	private static void onInvitationReceived(long senderId, string name)
	{
		string[] ignored = Groups.ignoreList.Value.Split(',');
		if (ignored.Any(s => string.Compare(s.Trim(), name, StringComparison.OrdinalIgnoreCase) == 0))
		{
			return;
		}

		if (Groups.blockInvitations.Value == Groups.BlockInvitation.Always || (Groups.blockInvitations.Value == Groups.BlockInvitation.PvP && Player.m_localPlayer?.IsPVPEnabled() == true))
		{
			return;
		}

		if (Groups.blockInvitations.Value == Groups.BlockInvitation.Enemy)
		{
			if (Player.m_localPlayer?.IsPVPEnabled() == true && Player.s_players.Any(p => p != Player.m_localPlayer && p.IsPVPEnabled() && (Groups.friendlyFire.Value == Groups.Toggle.On || Groups.ownGroup?.playerStates.ContainsKey(PlayerReference.fromPlayer(p)) != true) && Vector3.Distance(Player.m_localPlayer.transform.position, p.transform.position) < 30))
			{
				return;
			}
		}
		
		if (IsPlayerInCombat(Player.m_localPlayer))
		{
			ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "Groups CombatNotification", Player.m_localPlayer?.GetHoverName());
			return;
		}


		pendingInvitationSenderId = senderId;

		UnifiedPopup.Push(new YesNoPopup("$groups_invitation_received_title", Localization.instance.Localize("$groups_invitation_received_description", name), () =>
		{
			Groups.ownGroup?.Leave();

			UnifiedPopup.Pop();

			ZPackage playerState = new();
			Group.PlayerState.fromLocal().write(playerState);
			ZRoutedRpc.instance.InvokeRoutedRPC(pendingInvitationSenderId, "Groups AcceptInvitation", playerState);
		}, UnifiedPopup.Pop));

		API.InvokeInvitationReceived(PlayerReference.fromPlayerId(senderId), UnifiedPopup.instance.popupUIParent);
	}

	private static void onForcedInvitationReceived(long senderId)
	{
		API.JoinGroup(PlayerReference.fromPlayerId(senderId));
	}

	private static void onCombatNotificationReceived(long senderId, string playerName)
	{
		Chat.instance.AddString(Localization.instance.Localize("$groups_invitation_denied_in_combat", playerName));
	}

	private static void onInvitationAccepted(long senderId, ZPackage playerState)
	{
		ZPackage group = new();

		if (Groups.ownGroup is not null && Groups.ownGroup.AddMember(PlayerReference.fromPlayerId(senderId), Group.PlayerState.read(playerState)))
		{
			void AddPlayer(PlayerReference player)
			{
				group.Write(player.ToString());
				Groups.ownGroup!.playerStates[player].write(group);
			}

			AddPlayer(Groups.ownGroup.leader);
			group.Write(Groups.ownGroup.playerStates.Count - 1);
			foreach (PlayerReference player in Groups.ownGroup.playerStates.Keys.Where(p => p != Groups.ownGroup.leader))
			{
				AddPlayer(player);
			}
			ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "Groups AcceptInvitationResponse", group);

			return;
		}

		ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "Groups AcceptInvitationResponse", group);
	}

	private static void onInvitationAcceptedResponse(long senderId, ZPackage group)
	{
		if (group.Size() == 0)
		{
			Chat.instance.AddString(Localization.instance.Localize("$groups_joining_failed"));

			return;
		}

		Group tmp = new(PlayerReference.fromString(group.ReadString()), Group.PlayerState.read(group));
		int memberCount = group.ReadInt();
		for (int i = 0; i < memberCount; ++i)
		{
			tmp.playerStates.Add(PlayerReference.fromString(group.ReadString()), Group.PlayerState.read(group));
		}

		Groups.ownGroup = tmp;

		API.InvokeGroupJoined();

		ChatCommands.UpdateAutoCompletion();
	}

	private static void onUpdatePosition(long senderId, Vector3 position)
	{
		List<ZNet.PlayerInfo> playerInfos = new();
		foreach (ZNet.PlayerInfo playerInfo in ZNet.instance.m_players)
		{
			ZNet.PlayerInfo info = playerInfo;
			if (info.m_characterID.UserID == senderId)
			{
				info.m_position = position;
			}
			playerInfos.Add(info);
		}
		ZNet.instance.m_players = playerInfos;
	}

	private static bool IsPlayerInCombat(Player? player)
	{
		List<Character> characters = [];
		if (player != null) Character.GetCharactersInRange(player.transform.position, 20f, characters);
		return characters.Any(character => character != null && character.GetComponent<MonsterAI>()?.IsAlerted() == true);
	}

}
