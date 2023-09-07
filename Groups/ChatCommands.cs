using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;

namespace Groups;

public static class ChatCommands
{
	private const string groupChatPlaceholder = "Write to group ...";
	private static bool groupChatActive => Chat.instance && Chat.instance.m_input.transform.Find("Text Area/Placeholder").GetComponent<TextMeshProUGUI>().text == groupChatPlaceholder;

	private static readonly List<Terminal.ConsoleCommand> terminalCommands = new();

	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	public class AddChatCommands
	{
		private static void Postfix()
		{
			terminalCommands.Clear();

			terminalCommands.Add(new Terminal.ConsoleCommand("invite", "invite someone to your group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length <= "invite".Length || Chat.instance == null)
				{
					return;
				}

				if (Player.m_localPlayer?.m_nview.GetZDO().GetBool("dead") != false)
				{
					args.Context.AddString("You are dead.");
					return;
				}

				string playerName = args.FullLine.Substring(7);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					args.Context.AddString("You cannot invite yourself.");
					return;
				}

				long targetId = ZNet.instance.m_players.FirstOrDefault(p => string.Compare(playerName, p.m_name, StringComparison.OrdinalIgnoreCase) == 0).m_characterID.UserID;
				if (targetId == 0)
				{
					args.Context.AddString($"{playerName} is not online.");
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
					args.Context.AddString($"Sent an invitation to {playerName}.");
				}
				else
				{
					args.Context.AddString("Only the leader of a group can send out invitations.");
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
					args.Context.AddString("You are not in a group.");

					return;
				}

				if (Groups.ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					args.Context.AddString("Only the leader of a group can remove members.");

					return;
				}

				string playerName = args.FullLine.Substring(7);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					args.Context.AddString("You cannot remove yourself. Please use /leave instead.");

					return;
				}

				if (!Groups.ownGroup.RemoveMember(Groups.ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					args.Context.AddString($"{playerName} is not in this group.");
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
					args.Context.AddString("You are not in a group.");

					return;
				}

				if (Groups.ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					args.Context.AddString("Only the leader of a group can promote someone.");

					return;
				}

				string playerName = args.FullLine.Substring(8);

				if (!Groups.ownGroup.PromoteMember(Groups.ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					args.Context.AddString($"{playerName} is not in this group.");
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
					args.Context.AddString("You are not in a group.");

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
					args.Context.AddString("You are not in a group.");

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
			}
			else if (placeholder.text == groupChatPlaceholder)
			{
				placeholder.text = Localization.instance.Localize("$chat_entertext");
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
			__instance.m_chatBuffer.Insert(insertIndex, "/p [text] Group chat");
			__instance.m_chatBuffer.Insert(insertIndex, "/p Toggle group chat");
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
