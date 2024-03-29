﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;

namespace Groups;

public static class ChatCommands
{
	private static string groupChatPlaceholder = null!;
	private static bool groupChatActive => Chat.instance && Chat.instance.m_input.transform.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>().text == groupChatPlaceholder;

	private static readonly List<Terminal.ConsoleCommand> terminalCommands = new();

	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	public class AddChatCommands
	{
		private static void Postfix()
		{
			groupChatPlaceholder = Localization.instance.Localize("$groups_chat_placeholder");
			
			terminalCommands.Clear();

			terminalCommands.Add(new Terminal.ConsoleCommand("invite", "invite someone to your group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length <= "invite".Length || Chat.instance == null)
				{
					return;
				}

				if (Player.m_localPlayer?.m_nview.GetZDO().GetBool("dead") != false)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_dead"));
					return;
				}

				string playerName = args.FullLine.Substring(7);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_cannot_invite_self"));
					return;
				}

				long targetId = ZNet.instance.m_players.FirstOrDefault(p => string.Compare(playerName, p.m_name, StringComparison.OrdinalIgnoreCase) == 0).m_characterID.UserID;
				if (targetId == 0)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_player_not_online", playerName));
					return;
				}

				if (Groups.ownGroup is null)
				{
					Groups.ownGroup = new Group(PlayerReference.fromPlayer(Player.m_localPlayer), Group.PlayerState.fromLocal());
					API.InvokeGroupJoined();
				}

				if (Groups.ownGroup.leader == PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(targetId, "Groups InvitePlayer", Player.m_localPlayer.GetHoverName());
					args.Context.AddString(Localization.instance.Localize("$groups_invitation_sent", playerName));
				}
				else
				{
					args.Context.AddString(Localization.instance.Localize("$groups_leader_can_invite"));
				}
			}), optionsFetcher: () => ZNet.instance.m_players.Select(p => p.m_name).ToList()));

			terminalCommands.Add(new Terminal.ConsoleCommand("remove", "removes someone from your group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length <= "remove".Length || Chat.instance == null)
				{
					return;
				}

				if (Groups.ownGroup is null)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_not_in_group"));

					return;
				}

				if (Groups.ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					args.Context.AddString(Localization.instance.Localize("$groups_leader_can_remove"));

					return;
				}

				string playerName = args.FullLine.Substring(7);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_removed_self"));

					return;
				}

				if (!Groups.ownGroup.RemoveMember(Groups.ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					args.Context.AddString(Localization.instance.Localize("$groups_target_not_in_group", playerName));
				}

			}), optionsFetcher: () => Groups.ownGroup?.playerStates.Keys.Select(p => p.name).Where(n => n != Player.m_localPlayer.GetHoverName()).ToList() ?? new List<string>()));

			terminalCommands.Add(new Terminal.ConsoleCommand("promote", "promotes someone to group leader", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length <= "promote".Length || Chat.instance == null)
				{
					return;
				}

				if (Groups.ownGroup is null)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_not_in_group"));

					return;
				}

				if (Groups.ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					args.Context.AddString(Localization.instance.Localize("$groups_leader_can_promote"));

					return;
				}

				string playerName = args.FullLine.Substring(8);

				if (!Groups.ownGroup.PromoteMember(Groups.ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					args.Context.AddString(Localization.instance.Localize("$groups_target_not_in_group", playerName));
				}

			}), optionsFetcher: () => Groups.ownGroup?.playerStates.Keys.Select(p => p.name).Where(n => n != Player.m_localPlayer.GetHoverName()).ToList() ?? new List<string>()));

			_ = new Terminal.ConsoleCommand("leave", "leaves your current group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "leave".Length || Chat.instance == null)
				{
					return;
				}

				if (Groups.ownGroup is null)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_not_in_group"));

					return;
				}

				Groups.ownGroup.Leave();
			}));

			_ = new Terminal.ConsoleCommand("p", "toggles the group chat on", (Terminal.ConsoleEvent)(args =>
			{
				if (Chat.instance == null)
				{
					return;
				}

				if (Groups.ownGroup is null)
				{
					args.Context.AddString(Localization.instance.Localize("$groups_not_in_group"));

					return;
				}

				if (args.FullLine.Length > 2)
				{
					string message = args.FullLine.Substring(2);

					foreach (PlayerReference player in Groups.ownGroup.playerStates.Keys)
					{
						ZRoutedRpc.instance.InvokeRoutedRPC(player.peerId, "Groups ChatMessage", UserInfo.GetLocalUser(), message);
					}
				}
				else
				{
					ToggleGroupsChat(!groupChatActive);
				}
			}));
		}
	}

	public static void ToggleGroupsChat(bool active)
	{
		if (Chat.instance)
		{
			TextMeshProUGUI placeholder = Chat.instance.m_input.transform.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>();
			if (active)
			{
				placeholder.text = groupChatPlaceholder;
				Localization.instance.textMeshStrings[placeholder] = groupChatPlaceholder;
			}
			else if (placeholder.text == groupChatPlaceholder)
			{
				placeholder.text = Localization.instance.Localize("$chat_entertext");
				Localization.instance.textMeshStrings[placeholder] = "$chat_entertext";
			}
		}
	}

	public static void UpdateAutoCompletion()
	{
		foreach (Terminal.ConsoleCommand command in terminalCommands)
		{
			command.m_tabOptions = null;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.Awake))]
	public class AddGroupChat
	{
		private static void Postfix(Chat __instance)
		{
			int insertIndex = Math.Max(0, __instance.m_chatBuffer.Count - 5);
			__instance.m_chatBuffer.Insert(insertIndex,Localization.instance.Localize("$groups_group_chat_message_hint"));
			__instance.m_chatBuffer.Insert(insertIndex, Localization.instance.Localize("$groups_group_chat_toggle_hint"));
			__instance.UpdateChat();
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.InputText))]
	public class SendMessageToGroup
	{
		private static void Prefix(Chat __instance)
		{
			if (__instance.m_input.text.Length != 0 && groupChatActive && __instance.m_input.text[0] != '/')
			{
				__instance.m_input.text = "/p " + __instance.m_input.text;
			}
		}
	}
}
